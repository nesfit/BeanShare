using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.CoffeeStock.Queries.GetSpaceLedger;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;
using NSubstitute;

namespace BeanShare.Application.Tests.Features;

public sealed class LedgerQueryTests
{
    private readonly IClock _clock = TestClock.Instance;
    private readonly SpaceId _spaceId = SpaceId.New();
    private readonly UserId _admin = new(Guid.NewGuid());
    private readonly CoffeeProduct _product = CoffeeProduct.Create("Keňa Duma", "Coffeespot", CoffeeType.Filter);

    [Fact]
    public async Task Ledger_MergesPurchasesAndCups_WithRunningBalanceAndTotals()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        stock.AddPurchase(_product, Weight.FromGrams(1000), Money.Create(20m, "USD"), "Store", _admin, _clock.UtcNow.AddDays(-10), _clock);
        stock.AddPurchase(_product, Weight.FromGrams(1000), Money.Create(21m, "USD"), "Store", _admin, _clock.UtcNow.AddDays(-2), _clock);
        var bag1 = stock.Purchases.First(p => p.Label == "#1");
        var bag2 = stock.Purchases.First(p => p.Label == "#2");

        var cups = new List<ConsumptionEntry>
        {
            Cup(18, _clock.UtcNow.AddDays(-9), bag1.Id, 0.36m),
            Cup(9, _clock.UtcNow.AddDays(-1), bag2.Id, 0.18m),
        };

        var handler = CreateHandler(stock, cups);
        var result = await handler.Handle(new GetSpaceLedgerQuery(_spaceId.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var page = result.Value;
        page.TotalCount.Should().Be(4);
        page.Entries.Should().HaveCount(4);

        page.Totals.PurchasedGrams.Should().Be(2000m);
        page.Totals.ConsumedGrams.Should().Be(27m);
        page.Totals.NetGrams.Should().Be(1973m);
        page.Totals.PurchaseCount.Should().Be(2);
        page.Totals.CupCount.Should().Be(2);

        // Per-product running balance, chronologically: +1000, −18, +1000, −9.
        page.Entries.Single(e => e.Kind == "Purchase" && e.BagLabel == "#1").RunningBalanceGrams.Should().Be(1000m);
        var lastCup = page.Entries.Single(e => e.Kind == "Cup" && e.BagLabel == "#2");
        lastCup.RunningBalanceGrams.Should().Be(1973m);
        lastCup.DeltaGrams.Should().Be(-9m);
        lastCup.CostAmount.Should().Be(0.18m);
    }

    [Fact]
    public async Task Ledger_FilterCupsOnly_ReturnsOnlyOutflows()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        stock.AddPurchase(_product, Weight.FromGrams(1000), Money.Create(20m, "USD"), "Store", _admin, _clock.UtcNow.AddDays(-10), _clock);
        var bag1 = stock.Purchases.First();
        var cups = new List<ConsumptionEntry> { Cup(18, _clock.UtcNow.AddDays(-9), bag1.Id, 0.36m) };

        var handler = CreateHandler(stock, cups);
        var result = await handler.Handle(new GetSpaceLedgerQuery(_spaceId.Value, Kind: "cup"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Entries.Should().OnlyContain(e => e.Kind == "Cup" && e.DeltaGrams < 0);
        result.Value.Totals.PurchaseCount.Should().Be(0);
        result.Value.Totals.CupCount.Should().Be(1);
    }

    [Fact]
    public async Task Ledger_CupsOfAClosedOrSettledPeriod_AreNotEditable()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        stock.AddPurchase(_product, Weight.FromGrams(1000), Money.Create(20m, "USD"), "Store", _admin, _clock.UtcNow.AddDays(-90), _clock);
        var bag = stock.Purchases.First().Id;

        var settled = BillingPeriod.Create(_spaceId, "Settled", _clock.UtcNow.AddDays(-80), _clock.UtcNow.AddDays(-61), _admin, _clock);
        settled.Open(_admin, _clock);
        settled.Close(_admin, _clock);
        settled.MarkAsSettled(_admin, _clock);
        var closed = BillingPeriod.Create(_spaceId, "Closed", _clock.UtcNow.AddDays(-60), _clock.UtcNow.AddDays(-31), _admin, _clock);
        closed.Open(_admin, _clock);
        closed.Close(_admin, _clock);
        var open = BillingPeriod.Create(_spaceId, "Open", _clock.UtcNow.AddDays(-30), null, _admin, _clock);
        open.Open(_admin, _clock);

        var inSettled = Cup(10, _clock.UtcNow.AddDays(-70), bag, 0.2m, settled.Id);
        var inClosed = Cup(11, _clock.UtcNow.AddDays(-50), bag, 0.22m, closed.Id);
        var inOpen = Cup(12, _clock.UtcNow.AddDays(-20), bag, 0.24m, open.Id);
        var unstamped = Cup(13, _clock.UtcNow.AddDays(-1), bag, 0.26m);

        var handler = CreateHandler(stock, [inSettled, inClosed, inOpen, unstamped], [settled, closed, open]);
        var result = await handler.Handle(new GetSpaceLedgerQuery(_spaceId.Value, Kind: "cup"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Entries.ToDictionary(e => e.ConsumptionEntryId!.Value, e => (e.CanEdit, e.CanDelete)).Should().Equal(
            new Dictionary<Guid, (bool, bool)>
            {
                [inSettled.Id.Value] = (false, false),
                [inClosed.Id.Value] = (false, false),
                [inOpen.Id.Value] = (true, true),
                [unstamped.Id.Value] = (true, true),
            });
    }

    [Fact]
    public async Task Ledger_CupsBeforeTheLedgerStart_AreNotEditable()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        stock.AddPurchase(_product, Weight.FromGrams(1000), Money.Create(20m, "USD"), "Store", _admin, _clock.UtcNow.AddDays(-30), _clock);
        var bag = stock.Purchases.First().Id;
        var before = Cup(10, _clock.UtcNow.AddDays(-20), bag, 0.2m);
        var after = Cup(11, _clock.UtcNow.AddDays(-5), bag, 0.22m);

        var handler = CreateHandler(stock, [before, after], [], ledgerStart: _clock.UtcNow.AddDays(-10));
        var result = await handler.Handle(new GetSpaceLedgerQuery(_spaceId.Value, Kind: "cup"), CancellationToken.None);

        result.Value.Entries.Single(e => e.ConsumptionEntryId == before.Id.Value).Should().Match<LedgerEntryDto>(e => !e.CanEdit && !e.CanDelete);
        result.Value.Entries.Single(e => e.ConsumptionEntryId == after.Id.Value).Should().Match<LedgerEntryDto>(e => e.CanEdit && e.CanDelete);
    }

    [Fact]
    public async Task Ledger_AWriteOffShare_CanBeDeletedButNotEdited()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        stock.AddPurchase(_product, Weight.FromGrams(1000), Money.Create(20m, "USD"), "Store", _admin, _clock.UtcNow.AddDays(-30), _clock);
        var bag = stock.Purchases.First().Id;

        var open = BillingPeriod.Create(_spaceId, "Open", _clock.UtcNow.AddDays(-30), null, _admin, _clock);
        open.Open(_admin, _clock);
        var closed = BillingPeriod.Create(_spaceId, "Closed", _clock.UtcNow.AddDays(-60), _clock.UtcNow.AddDays(-31), _admin, _clock);
        closed.Open(_admin, _clock);
        closed.Close(_admin, _clock);

        var cup = Cup(10, _clock.UtcNow.AddDays(-3), bag, 0.2m, open.Id);
        var share = Cup(40, _clock.UtcNow.AddDays(-2), bag, 0.8m, open.Id, writeOff: true);
        var frozenShare = Cup(40, _clock.UtcNow.AddDays(-40), bag, 0.8m, closed.Id, writeOff: true);

        var handler = CreateHandler(stock, [cup, share, frozenShare], [open, closed]);
        var result = await handler.Handle(new GetSpaceLedgerQuery(_spaceId.Value, Kind: "cup"), CancellationToken.None);

        result.Value.Entries.ToDictionary(e => e.ConsumptionEntryId!.Value, e => (e.CanEdit, e.CanDelete)).Should().Equal(
            new Dictionary<Guid, (bool, bool)>
            {
                [cup.Id.Value] = (true, true),
                [share.Id.Value] = (false, true),
                [frozenShare.Id.Value] = (false, false),
            });
    }

    private ConsumptionEntry Cup(int grams, DateTime at, Guid lotId, decimal cost, BillingPeriodId? period = null, bool writeOff = false)
    {
        var cup = ConsumptionEntry.Create(
            _spaceId, _admin, CoffeeProduct.Create(_product.Name, _product.Brand, _product.Type),
            Weight.FromGrams(grams), at, _clock,
            stockLotId: lotId, cost: Money.Create(cost, "USD"), unitCostPerGram: 0.02m, isWriteOff: writeOff);
        if (period is { } stamped)
            cup.AssignToBillingPeriod(stamped);
        return cup;
    }

    private GetSpaceLedgerQueryHandler CreateHandler(
        CoffeeStock stock, List<ConsumptionEntry> cups, List<BillingPeriod>? periods = null, DateTime? ledgerStart = null)
    {
        var coffeeStockRepo = Substitute.For<ICoffeeStockRepository>();
        coffeeStockRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(stock);

        var consumptionRepo = Substitute.For<IConsumptionRepository>();
        consumptionRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(cups);

        var space = Domain.Aggregates.Space.Space.Create(_spaceId, "Lab", Currency.USD, _admin, new InviteCode("CAFE23"), _clock);
        if (ledgerStart.HasValue)
            space.SetLedgerStartDate(ledgerStart, hasLedgerSettlement: false);
        var spaceRepo = Substitute.For<ISpaceRepository>();
        spaceRepo.GetSingleBySpecAsync(Arg.Any<Domain.Specifications.SpaceByIdSpecification>(), Arg.Any<CancellationToken>()).Returns(space);

        var periodRepo = Substitute.For<IBillingPeriodRepository>();
        periodRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(periods ?? []);

        var userRepo = Substitute.For<IUserRepository>();
        userRepo.GetByIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var userContext = Substitute.For<IUserContext>();
        userContext.CurrentUserId.Returns(_admin);

        return new GetSpaceLedgerQueryHandler(coffeeStockRepo, consumptionRepo, spaceRepo, periodRepo, userRepo, userContext);
    }
}
