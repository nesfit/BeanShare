using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using NSubstitute;

namespace BeanShare.Application.Tests.Features;

public sealed class BagCommandTests
{
    private readonly IClock _clock = TestClock.Instance;
    private readonly SpaceId _spaceId = SpaceId.New();
    private readonly UserId _admin = new(Guid.NewGuid());
    private readonly UserId _memberB = new(Guid.NewGuid());
    private readonly CoffeeProduct _product = CoffeeProduct.Create("Keňa Duma", "Coffeespot", CoffeeType.Filter);

    private Domain.Aggregates.Space.Space CreateSpace()
    {
        var space = Domain.Aggregates.Space.Space.Create(_spaceId, "Lab", Currency.USD, _admin, new InviteCode("CAFE23"), _clock);
        space.Join(_memberB, _clock);
        return space;
    }

    private CoffeeStock StockWithOneBag(int grams)
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        stock.AddPurchase(_product, Weight.FromGrams(grams), Money.Create(20m, "USD"), "Store", _admin, _clock.UtcNow.AddDays(-1), _clock);
        return stock;
    }

    [Fact]
    public async Task OpenBag_AsAdmin_MarksBagOpen()
    {
        var space = CreateSpace();
        var stock = CoffeeStock.Create(_spaceId, _clock);
        stock.AddPurchase(_product, Weight.FromGrams(1000), Money.Create(20m, "USD"), "S", _admin, _clock.UtcNow.AddDays(-2), _clock);
        stock.AddPurchase(_product, Weight.FromGrams(1000), Money.Create(21m, "USD"), "S", _admin, _clock.UtcNow.AddDays(-1), _clock);
        var bag2 = stock.Purchases.First(p => p.Label == "#2");

        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var handler = new OpenBagCommandHandler(coffeeStockRepo, spaceRepo, userContext, _clock);

        var result = await handler.Handle(new OpenBagCommand(_spaceId.Value, bag2.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bag2.IsOpen.Should().BeTrue();
        await coffeeStockRepo.Received(1).UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenBag_AsNonAdmin_IsRejected()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(1000);
        var bagId = stock.Purchases.First().Id;

        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        userContext.CurrentUserId.Returns(_memberB); // not admin

        var handler = new OpenBagCommandHandler(coffeeStockRepo, spaceRepo, userContext, _clock);

        var result = await handler.Handle(new OpenBagCommand(_spaceId.Value, bagId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await coffeeStockRepo.DidNotReceive().UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkBagEmpty_DistributesRemainderToBagDrinkers_AndEmptiesBag()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(1000);
        var bag = stock.Purchases.First();

        // Prior logged cups from THIS bag: admin 100 g, member B 300 g (400 g consumed → 600 g remaining).
        stock.ConsumeStock(_product, Weight.FromGrams(100), _clock, bag.Id);
        stock.ConsumeStock(_product, Weight.FromGrams(300), _clock, bag.Id);

        var priorCups = new List<ConsumptionEntry>
        {
            Cup(_admin, 100, bag.Id),
            Cup(_memberB, 300, bag.Id),
        };

        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var consumptionRepo = Substitute.For<IConsumptionRepository>();
        consumptionRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(priorCups);

        var added = new List<ConsumptionEntry>();
        consumptionRepo.AddAsync(Arg.Do<ConsumptionEntry>(added.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var handler = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, _clock);

        var result = await handler.Handle(new MarkBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DistributedGrams.Should().Be(600m);
        result.Value.MemberCount.Should().Be(2);

        // Bag is fully emptied.
        bag.IsEmpty.Should().BeTrue();
        bag.RemainingGrams.Should().Be(Weight.Zero);

        // Remainder billed pro-rata by prior consumption: admin 25% → 150 g, member B 75% → 450 g.
        added.Should().HaveCount(2);
        added.Sum(e => e.Quantity.Grams).Should().Be(600m);
        added.Should().OnlyContain(e => e.StockLotId == bag.Id);
        added.Single(e => e.UserId == _admin).Quantity.Grams.Should().Be(150m);
        added.Single(e => e.UserId == _memberB).Quantity.Grams.Should().Be(450m);
        // Each phantom cup is cost-stamped at the bag's €/g ($20/1000 g = $0.02/g).
        added.Should().OnlyContain(e => e.Cost != null);
    }

    [Fact]
    public async Task MarkBagEmpty_WithNoConsumptionHistory_SplitsEquallyAmongMembers()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(1000); // untouched bag, 1000 g remaining, no cups
        var bag = stock.Purchases.First();

        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var consumptionRepo = Substitute.For<IConsumptionRepository>();
        consumptionRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>())
            .Returns(new List<ConsumptionEntry>());
        var added = new List<ConsumptionEntry>();
        consumptionRepo.AddAsync(Arg.Do<ConsumptionEntry>(added.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var handler = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, _clock);

        var result = await handler.Handle(new MarkBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        bag.IsEmpty.Should().BeTrue();
        added.Should().HaveCount(2); // admin + member B
        added.Sum(e => e.Quantity.Grams).Should().Be(1000m); // exact, residual folded in
    }

    // ---- UndoBagEmpty ----

    [Fact]
    public async Task UndoBagEmpty_SealedBagEmptiedByMistake_RestoresItToSealedAndRemovesBilledCups()
    {
        // The real-life mis-click: a 1000 g bag nobody had opened yet gets "emptied" and the whole
        // kilogram is billed to the space's drinkers.
        var space = CreateSpace();
        var stock = StockWithOneBag(1000);
        var bag = stock.Purchases.First();
        var clock = new MutableClock(_clock.UtcNow);
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var consumptionRepo = new InMemoryConsumptions();

        var empty = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, clock);
        (await empty.Handle(new MarkBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();
        bag.IsEmpty.Should().BeTrue();
        bag.WrittenOffGrams.Should().Be(1000m);
        consumptionRepo.Entries.Should().HaveCount(2).And.OnlyContain(e => e.IsWriteOff);

        clock.Advance(TimeSpan.FromMinutes(5)); // admin notices a few minutes later

        var undo = new UndoBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, BillingPeriods(), spaceRepo, userContext, clock);
        var result = await undo.Handle(new UndoBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RestoredGrams.Should().Be(1000m);
        result.Value.RemovedEntries.Should().Be(2);
        result.Value.NewStatus.Should().Be("Sealed");

        bag.Status.Should().Be(LotStatus.Sealed);
        bag.RemainingGrams.Should().Be(Weight.FromGrams(1000));
        bag.WrittenOffGrams.Should().Be(0m);
        bag.OpenedAt.Should().BeNull();
        bag.DepletedAt.Should().BeNull();
        consumptionRepo.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task UndoBagEmpty_PartiallyUsedBag_RevivesAsOpenAndKeepsRealCups()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(1000);
        var bag = stock.Purchases.First();
        var clock = new MutableClock(_clock.UtcNow);
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var consumptionRepo = new InMemoryConsumptions();

        // A real cup: 100 g poured from this bag (opens it), logged as consumption.
        stock.ConsumeStock(_product, Weight.FromGrams(100), clock, bag.Id);
        var realCup = Cup(_admin, 100, bag.Id);
        consumptionRepo.Entries.Add(realCup);

        var empty = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, clock);
        (await empty.Handle(new MarkBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();
        bag.WrittenOffGrams.Should().Be(900m);

        var undo = new UndoBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, BillingPeriods(), spaceRepo, userContext, clock);
        var result = await undo.Handle(new UndoBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RestoredGrams.Should().Be(900m);
        bag.Status.Should().Be(LotStatus.Open);
        bag.RemainingGrams.Should().Be(Weight.FromGrams(900));
        bag.ConsumedGrams.Should().Be(100m);
        consumptionRepo.Entries.Should().ContainSingle().Which.Should().BeSameAs(realCup);
    }

    [Fact]
    public async Task UndoBagEmpty_AfterUndoWindow_IsRejected()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(1000);
        var bag = stock.Purchases.First();
        var clock = new MutableClock(_clock.UtcNow);
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var consumptionRepo = new InMemoryConsumptions();

        var empty = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, clock);
        (await empty.Handle(new MarkBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();

        clock.Advance(Purchase.WriteOffUndoWindow + TimeSpan.FromMinutes(1));

        var undo = new UndoBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, BillingPeriods(), spaceRepo, userContext, clock);
        var result = await undo.Handle(new UndoBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == Error.Codes.CannotUndoBagEmpty && e.Message.Contains("window"));
        bag.IsEmpty.Should().BeTrue();
        consumptionRepo.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task UndoBagEmpty_BagThatRanOutCupByCup_IsRejected()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(20);
        var bag = stock.Purchases.First();
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var consumptionRepo = new InMemoryConsumptions();

        // Two real cups drain the bag; nothing was written off.
        stock.ConsumeStock(_product, Weight.FromGrams(10), _clock, bag.Id);
        stock.ConsumeStock(_product, Weight.FromGrams(10), _clock, bag.Id);
        consumptionRepo.Entries.Add(Cup(_admin, 10, bag.Id));
        consumptionRepo.Entries.Add(Cup(_memberB, 10, bag.Id));
        bag.IsEmpty.Should().BeTrue();
        bag.WrittenOffGrams.Should().Be(0m);

        var undo = new UndoBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, BillingPeriods(), spaceRepo, userContext, _clock);
        var result = await undo.Handle(new UndoBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == Error.Codes.CannotUndoBagEmpty);
        consumptionRepo.Entries.Should().HaveCount(2); // real cups untouched
    }

    [Fact]
    public async Task UndoBagEmpty_AsNonAdmin_IsRejected()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(1000);
        var bag = stock.Purchases.First();
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var consumptionRepo = new InMemoryConsumptions();

        var empty = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, _clock);
        (await empty.Handle(new MarkBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();

        userContext.CurrentUserId.Returns(_memberB); // not admin
        var undo = new UndoBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, BillingPeriods(), spaceRepo, userContext, _clock);
        var result = await undo.Handle(new UndoBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        bag.IsEmpty.Should().BeTrue();
        consumptionRepo.Entries.Should().HaveCount(2);
    }

    // ---- DiscardBag ----

    [Fact]
    public async Task DiscardBag_ThrowsRemainderAwayWithoutBillingAnyone()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(1000);
        var bag = stock.Purchases.First();
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);

        // 100 g really drunk; the other 900 g went stale.
        stock.ConsumeStock(_product, Weight.FromGrams(100), _clock, bag.Id);

        var handler = new DiscardBagCommandHandler(coffeeStockRepo, spaceRepo, userContext, _clock);
        var result = await handler.Handle(new DiscardBagCommand(_spaceId.Value, bag.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DiscardedGrams.Should().Be(900m);
        result.Value.SunkCostAmount.Should().Be(18m); // $20 / 1000 g × 900 g
        result.Value.Currency.Should().Be("USD");

        bag.IsEmpty.Should().BeTrue();
        bag.RemainingGrams.Should().Be(Weight.Zero);
        bag.ConsumedGrams.Should().Be(100m);     // only the real cup counts as consumed
        bag.DiscardedGrams.Should().Be(900m);
        bag.WrittenOffGrams.Should().Be(0m);      // nothing was billed
        stock.StockLevels.Single().CurrentStock.Should().Be(Weight.Zero);
        stock.StockLevels.Single().TotalConsumed.Should().Be(Weight.FromGrams(100));
    }

    [Fact]
    public async Task DiscardBag_AlreadyEmpty_IsRejected()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(10);
        var bag = stock.Purchases.First();
        stock.ConsumeStock(_product, Weight.FromGrams(10), _clock, bag.Id);
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);

        var handler = new DiscardBagCommandHandler(coffeeStockRepo, spaceRepo, userContext, _clock);
        var result = await handler.Handle(new DiscardBagCommand(_spaceId.Value, bag.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await coffeeStockRepo.DidNotReceive().UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscardBag_AsNonAdmin_IsRejected()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(1000);
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        userContext.CurrentUserId.Returns(_memberB);

        var handler = new DiscardBagCommandHandler(coffeeStockRepo, spaceRepo, userContext, _clock);
        var result = await handler.Handle(new DiscardBagCommand(_spaceId.Value, stock.Purchases.First().Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        stock.Purchases.First().IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task UndoBagEmpty_AfterDiscard_PutsGramsBackWithNoCupsToRemove()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(1000);
        var bag = stock.Purchases.First();
        var clock = new MutableClock(_clock.UtcNow);
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var consumptionRepo = new InMemoryConsumptions();

        var discard = new DiscardBagCommandHandler(coffeeStockRepo, spaceRepo, userContext, clock);
        (await discard.Handle(new DiscardBagCommand(_spaceId.Value, bag.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();
        bag.DiscardedGrams.Should().Be(1000m);

        clock.Advance(TimeSpan.FromHours(3));

        var undo = new UndoBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, BillingPeriods(), spaceRepo, userContext, clock);
        var result = await undo.Handle(new UndoBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RestoredGrams.Should().Be(1000m);
        result.Value.RemovedEntries.Should().Be(0);
        result.Value.NewStatus.Should().Be("Sealed");
        bag.Status.Should().Be(LotStatus.Sealed);
        bag.RemainingGrams.Should().Be(Weight.FromGrams(1000));
        bag.DiscardedGrams.Should().Be(0m);
        bag.DepletedAt.Should().BeNull();
    }

    private static IBillingPeriodRepository BillingPeriods()
    {
        var repo = Substitute.For<IBillingPeriodRepository>();
        repo.GetByIdAsync(Arg.Any<BillingPeriodId>(), Arg.Any<CancellationToken>())
            .Returns((Domain.Aggregates.BillingPeriod.BillingPeriod?)null);
        return repo;
    }

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTime start) => UtcNow = start;
        public DateTime UtcNow { get; private set; }
        public void Advance(TimeSpan by) => UtcNow += by;
    }

    /// <summary>Just enough of a consumption store for the empty → undo round trip (add, spec query, remove).</summary>
    private sealed class InMemoryConsumptions : IConsumptionRepository
    {
        public List<ConsumptionEntry> Entries { get; } = new();

        public Task AddAsync(ConsumptionEntry consumption, CancellationToken cancellationToken = default)
        {
            Entries.Add(consumption);
            return Task.CompletedTask;
        }

        public Task<ConsumptionEntry?> GetByIdAsync(ConsumptionEntryId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));

        public Task<IEnumerable<ConsumptionEntry>> GetBySpaceIdAsync(SpaceId spaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<ConsumptionEntry>>(Entries.Where(e => e.SpaceId == spaceId).ToList());

        public Task<IReadOnlyList<ConsumptionEntry>> GetRecentBySpaceIdAsync(SpaceId spaceId, int limit, UserId? forUserId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConsumptionEntry>>(Entries.Where(e => e.SpaceId == spaceId).Take(limit).ToList());

        public Task<IReadOnlyList<ConsumptionEntry>> GetBySpecAsync(Domain.Specifications.ISpec<ConsumptionEntry> specification, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConsumptionEntry>>(Entries.Where(specification.Criteria.Compile()).ToList());

        public Task<(IReadOnlyList<ConsumptionEntry> Items, int TotalCount)> GetPagedBySpecAsync(Domain.Specifications.ISpec<ConsumptionEntry> specification, int skip, int take, CancellationToken cancellationToken = default)
        {
            var all = Entries.Where(specification.Criteria.Compile()).ToList();
            return Task.FromResult<(IReadOnlyList<ConsumptionEntry>, int)>((all.Skip(skip).Take(take).ToList(), all.Count));
        }

        public Task<(decimal TotalGrams, int TotalEntries, int UniqueDays)> GetSummaryBySpecAsync(Domain.Specifications.ISpec<ConsumptionEntry> specification, CancellationToken cancellationToken = default)
        {
            var all = Entries.Where(specification.Criteria.Compile()).ToList();
            return Task.FromResult((all.Sum(e => e.Quantity.Grams), all.Count, all.Select(e => e.ConsumedAt.Date).Distinct().Count()));
        }

        public Task UpdateAsync(ConsumptionEntry consumption, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(ConsumptionEntry consumption, CancellationToken cancellationToken = default)
        {
            Entries.Remove(consumption);
            return Task.CompletedTask;
        }
    }

    private ConsumptionEntry Cup(UserId user, int grams, Guid lotId) =>
        ConsumptionEntry.Create(
            _spaceId, user, CoffeeProduct.Create(_product.Name, _product.Brand, _product.Type),
            Weight.FromGrams(grams), _clock.UtcNow.AddHours(-1), _clock, stockLotId: lotId);

    private (ICoffeeStockRepository, ISpaceRepository, IUserContext) Mocks(Domain.Aggregates.Space.Space space, CoffeeStock stock)
    {
        var coffeeStockRepo = Substitute.For<ICoffeeStockRepository>();
        coffeeStockRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(stock);
        var spaceRepo = Substitute.For<ISpaceRepository>();
        spaceRepo.GetSingleBySpecAsync(Arg.Any<Domain.Specifications.SpaceByIdSpecification>(), Arg.Any<CancellationToken>()).Returns(space);
        var userContext = Substitute.For<IUserContext>();
        userContext.CurrentUserId.Returns(_admin);
        return (coffeeStockRepo, spaceRepo, userContext);
    }
}
