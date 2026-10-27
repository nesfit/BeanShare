using BeanShare.Application.Tests.TestHelpers;
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

        var handler = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, _clock, TestUsers.ActiveRepository());

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

        var handler = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, _clock, TestUsers.ActiveRepository());

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

        var empty = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, clock, TestUsers.ActiveRepository());
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

        var empty = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, clock, TestUsers.ActiveRepository());
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

        var empty = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, clock, TestUsers.ActiveRepository());
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

        var empty = new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, _clock, TestUsers.ActiveRepository());
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

    // The bag's payer is credited, not necessarily the admin who recorded it.
    [Fact]
    public async Task AddPurchase_WithPaidBy_MakesThatMemberTheEffectivePayerOfEveryBag()
    {
        var space = CreateSpace();
        var stock = CoffeeStock.Create(_spaceId, _clock);
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var handler = new AddStockPurchaseCommandHandler(coffeeStockRepo, spaceRepo, userContext, _clock, Substitute.For<MapsterMapper.IMapper>(), TestUsers.ActiveRepository());

        var result = await handler.Handle(new AddStockPurchaseCommand(
            _spaceId.Value, _product.Name, _product.Brand, "Filter", 1000, 20m, "USD", "Store",
            _clock.UtcNow.AddDays(-1), BagCount: 2, PaidByUserId: _memberB.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stock.Purchases.Should().HaveCount(2);
        stock.Purchases.Should().OnlyContain(p => p.PurchasedBy == _admin && p.PaidByUserId == _memberB && p.EffectivePayer == _memberB);
    }

    [Fact]
    public async Task AddPurchase_WithoutPaidBy_CreditsTheRecorder()
    {
        var space = CreateSpace();
        var stock = CoffeeStock.Create(_spaceId, _clock);
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var handler = new AddStockPurchaseCommandHandler(coffeeStockRepo, spaceRepo, userContext, _clock, Substitute.For<MapsterMapper.IMapper>(), TestUsers.ActiveRepository());

        var result = await handler.Handle(new AddStockPurchaseCommand(
            _spaceId.Value, _product.Name, _product.Brand, "Filter", 1000, 20m, "USD", "Store", _clock.UtcNow.AddDays(-1)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stock.Purchases.Single().EffectivePayer.Should().Be(_admin);
    }

    [Fact]
    public async Task AddPurchase_WithAPayerOutsideTheSpace_Fails()
    {
        var space = CreateSpace();
        var stock = CoffeeStock.Create(_spaceId, _clock);
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var handler = new AddStockPurchaseCommandHandler(coffeeStockRepo, spaceRepo, userContext, _clock, Substitute.For<MapsterMapper.IMapper>(), TestUsers.ActiveRepository());

        var result = await handler.Handle(new AddStockPurchaseCommand(
            _spaceId.Value, _product.Name, _product.Brand, "Filter", 1000, 20m, "USD", "Store",
            _clock.UtcNow.AddDays(-1), PaidByUserId: Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors[0].Code.Should().Be(Error.Codes.MemberNotFound);
        stock.Purchases.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdatePurchase_WithPaidBy_ChangesThePayerAndBumpsTheBagVersion()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(1000);
        var bag = stock.Purchases.Single();
        var versionBefore = bag.Version;
        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var handler = new UpdateStockPurchaseCommandHandler(coffeeStockRepo, spaceRepo, userContext, _clock, TestUsers.ActiveRepository());

        var result = await handler.Handle(new UpdateStockPurchaseCommand(
            _spaceId.Value, bag.Id, 1000, 20m, bag.PurchasedAt, PaidByUserId: _memberB.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PaidByUserId.Should().Be(_memberB.Value);
        bag.EffectivePayer.Should().Be(_memberB);
        bag.Version.Should().BeGreaterThan(versionBefore);
    }

    // Write-offs bill only current members and stamp each bag drawn.
    [Fact]
    public async Task MarkBagEmpty_SkipsDrinkersWhoAreNoLongerMembers_AndRenormalises()
    {
        var space = CreateSpace();
        var stock = StockWithOneBag(1000);
        var bag = stock.Purchases.First();
        var exMember = new UserId(Guid.NewGuid());
        stock.ConsumeStock(_product, Weight.FromGrams(400), _clock, bag.Id);
        var priorCups = new List<ConsumptionEntry> { Cup(_memberB, 100, bag.Id), Cup(exMember, 300, bag.Id) };

        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var consumptionRepo = Substitute.For<IConsumptionRepository>();
        consumptionRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(priorCups);
        var added = new List<ConsumptionEntry>();
        consumptionRepo.AddAsync(Arg.Do<ConsumptionEntry>(added.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await new MarkBagEmptyCommandHandler(coffeeStockRepo, consumptionRepo, spaceRepo, userContext, _clock, TestUsers.ActiveRepository())
            .Handle(new MarkBagEmptyCommand(_spaceId.Value, bag.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().ContainSingle().Which.UserId.Should().Be(_memberB);
        added.Single().Quantity.Grams.Should().Be(600m);
    }

    [Fact]
    public async Task SplitRemainingStock_StampsOneEntryPerUserPerBagAndSkipsExMembers()
    {
        var space = CreateSpace();
        var stock = CoffeeStock.Create(_spaceId, _clock);
        stock.AddPurchase(_product, Weight.FromGrams(100), Money.Create(10m, "USD"), "S", _admin, _clock.UtcNow.AddDays(-2), _clock);
        stock.AddPurchase(_product, Weight.FromGrams(100), Money.Create(20m, "USD"), "S", _memberB, _clock.UtcNow.AddDays(-1), _clock);
        var bags = stock.Purchases.OrderBy(p => p.PurchasedAt).ToList();
        var exMember = new UserId(Guid.NewGuid());
        var consumptions = new InMemoryConsumptions();
        consumptions.Entries.Add(Cup(_admin, 10, bags[0].Id));
        consumptions.Entries.Add(Cup(_memberB, 30, bags[0].Id));
        consumptions.Entries.Add(Cup(exMember, 60, bags[0].Id));

        var (coffeeStockRepo, spaceRepo, userContext) = Mocks(space, stock);
        var userService = Substitute.For<Application.Services.IUserService>();
        userService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), Arg.Any<CancellationToken>()).Returns(new List<User>());

        var result = await new SplitRemainingStockCommandHandler(coffeeStockRepo, consumptions, spaceRepo, userService, userContext, _clock, TestUsers.ActiveRepository())
            .Handle(new SplitRemainingStockCommand(_spaceId.Value, stock.StockLevels.Single().Id), CancellationToken.None);

        // 200 g left over two bags, split 25:75 between the current members: admin 50 g, B 150 g.
        result.IsSuccess.Should().BeTrue();
        result.Value.Allocations.Should().NotContain(a => a.UserId == exMember.Value);
        var writeOffs = consumptions.Entries.Where(e => e.IsWriteOff).ToList();
        writeOffs.Should().OnlyContain(e => e.StockLotId != null && e.Cost != null);
        writeOffs.Where(e => e.UserId == _admin).Sum(e => e.Quantity.Grams).Should().Be(50m);
        writeOffs.Where(e => e.UserId == _memberB).Sum(e => e.Quantity.Grams).Should().Be(150m);
        writeOffs.Where(e => e.StockLotId == bags[0].Id).Sum(e => e.Quantity.Grams).Should().Be(100m);
        writeOffs.Where(e => e.StockLotId == bags[1].Id).Sum(e => e.Quantity.Grams).Should().Be(100m);
        writeOffs.Single(e => e.UserId == _memberB && e.StockLotId == bags[1].Id).Cost!.Amount.Should().Be(20m);
    }

    [Fact]
    public void ConsumeStock_RequiresASpaceAdmin()
    {
        typeof(ConsumeStockCommand).GetCustomAttributes(typeof(Application.Common.Authorization.RequireSpaceAdminAttribute), false)
            .Should().ContainSingle("drawing stock without a cup destroys the payer's credit");
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

        public Task<IReadOnlyList<ConsumptionEntry>> GetBySpecForUpdateAsync(Domain.Specifications.ISpec<ConsumptionEntry> specification, CancellationToken cancellationToken = default) =>
            GetBySpecAsync(specification, cancellationToken);

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
    // The equal split leaves placeholders out.
    [Fact]
    public async Task MarkBagEmpty_WithNoConsumptionHistory_SplitsEquallyAmongPeopleWithAccounts()
    {
        var world = new LedgerWorld("CZK");
        var alice = world.AddMember("Alice");
        world.AddPlaceholder("Old Pete");
        var bag = world.AddBag(1000m, 500m, world.Clock.UtcNow.AddDays(-1));

        var result = await new MarkBagEmptyCommandHandler(
                world.CoffeeStockRepository, world.ConsumptionRepository, world.SpaceRepository, world.UserContext, world.Clock, world.UserRepository)
            .Handle(new MarkBagEmptyCommand(world.Space.Id.Value, bag.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        world.Cups.Select(c => c.UserId).Should().BeEquivalentTo(new[] { world.Admin, alice });
        world.Cups.Sum(c => c.Quantity.Grams).Should().Be(1000m);
    }
    // Deleting write-off shares keeps the bag's counters honest.
    [Fact]
    public async Task DeletingWriteOffShares_ThenDiscardAndUndo_RestoresExactlyTheDiscard()
    {
        var world = new LedgerWorld("CZK");
        var alice = world.AddMember("Alice");
        var bag = world.AddBag(1000m, 500m, world.Clock.UtcNow.AddDays(-2));
        world.Drink(alice, 700m, world.Clock.UtcNow.AddDays(-1), bag);
        (await new MarkBagEmptyCommandHandler(
                world.CoffeeStockRepository, world.ConsumptionRepository, world.SpaceRepository, world.UserContext, world.Clock, world.UserRepository)
            .Handle(new MarkBagEmptyCommand(world.Space.Id.Value, bag.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();
        bag.WrittenOffGrams.Should().Be(300m);

        var delete = new BeanShare.Application.Features.Consumption.Commands.DeleteConsumptionCommandHandler(
            world.ConsumptionRepository, world.BillingPeriodRepository, world.CoffeeStockRepository, world.SpaceRepository, world.Clock);
        foreach (var share in world.Cups.Where(c => c.IsWriteOff).ToList())
            (await delete.Handle(new BeanShare.Application.Features.Consumption.Commands.DeleteConsumptionCommand(share.Id.Value, world.Space.Id.Value), CancellationToken.None))
                .IsSuccess.Should().BeTrue();

        bag.ConsumedGrams.Should().Be(700m);
        bag.WrittenOffGrams.Should().Be(0m);
        bag.Status.Should().Be(LotStatus.Open);

        world.Stock.DiscardBag(bag.Id, world.Clock).Grams.Should().Be(300m);
        var undo = await new UndoBagEmptyCommandHandler(world.CoffeeStockRepository, world.ConsumptionRepository, world.BillingPeriodRepository,
                world.SpaceRepository, world.UserContext, world.Clock)
            .Handle(new UndoBagEmptyCommand(world.Space.Id.Value, bag.Id), CancellationToken.None);

        undo.IsSuccess.Should().BeTrue();
        undo.Value.RestoredGrams.Should().Be(300m);
        bag.ConsumedGrams.Should().Be(700m);
        bag.RemainingGrams.Grams.Should().Be(300m);
    }

    // Like deleting a cup, "Undo empty" removes no share dated before the ledger start.
    [Fact]
    public async Task UndoingAnEmptying_WhoseSharesAreBeforeTheLedgerStart_IsRefused()
    {
        var world = new LedgerWorld("CZK");
        var alice = world.AddMember("Alice");
        var bag = world.AddBag(1000m, 500m, world.Clock.UtcNow.AddDays(-2));
        world.Drink(alice, 700m, world.Clock.UtcNow.AddDays(-1), bag);
        (await new MarkBagEmptyCommandHandler(
                world.CoffeeStockRepository, world.ConsumptionRepository, world.SpaceRepository, world.UserContext, world.Clock, world.UserRepository)
            .Handle(new MarkBagEmptyCommand(world.Space.Id.Value, bag.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();
        var shares = world.Cups.Where(c => c.IsWriteOff).ToList();

        // Emptied today; C is set to tomorrow, and the undo comes tomorrow morning, inside its 24 h window.
        var tomorrow = world.Clock.UtcNow.Date.AddDays(1);
        world.Space.SetLedgerStartDate(tomorrow, hasLedgerSettlement: false);
        world.Clock.Set(tomorrow.AddHours(8));
        bag.IsWriteOffUndoable(world.Clock.UtcNow).Should().BeTrue();

        var undo = await new UndoBagEmptyCommandHandler(world.CoffeeStockRepository, world.ConsumptionRepository, world.BillingPeriodRepository,
                world.SpaceRepository, world.UserContext, world.Clock)
            .Handle(new UndoBagEmptyCommand(world.Space.Id.Value, bag.Id), CancellationToken.None);

        undo.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.CannotUndoBagEmpty);
        undo.Errors[0].Message.Should().Contain("ledger start").And.Contain("move the ledger start back", "the refusal names the way out");
        world.Cups.Where(c => c.IsWriteOff).Should().BeEquivalentTo(shares);
        bag.WrittenOffGrams.Should().Be(300m);
        bag.RemainingGrams.Grams.Should().Be(0m);
    }

    // A write-off share cannot be edited, and the bag stays as it is.
    [Fact]
    public async Task EditingAWriteOffShare_IsRefused()
    {
        var world = new LedgerWorld("CZK");
        var alice = world.AddMember("Alice");
        var bag = world.AddBag(1000m, 500m, world.Clock.UtcNow.AddDays(-2));
        world.Drink(alice, 700m, world.Clock.UtcNow.AddDays(-1), bag);
        (await new MarkBagEmptyCommandHandler(
                world.CoffeeStockRepository, world.ConsumptionRepository, world.SpaceRepository, world.UserContext, world.Clock, world.UserRepository)
            .Handle(new MarkBagEmptyCommand(world.Space.Id.Value, bag.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();
        var share = world.Cups.First(c => c.IsWriteOff);
        var (consumed, writtenOff, version) = (bag.ConsumedGrams, bag.WrittenOffGrams, bag.Version);

        var result = await new BeanShare.Application.Features.Consumption.Commands.UpdateConsumptionCommandHandler(
                world.ConsumptionRepository, world.BillingPeriodRepository, world.CoffeeStockRepository, world.SpaceRepository, world.Clock)
            .Handle(new BeanShare.Application.Features.Consumption.Commands.UpdateConsumptionCommand(
                share.Id.Value, world.Space.Id.Value, "Alfa", "Roaster", "Filter", 10m, share.ConsumedAt), CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("delete it instead");
        (bag.ConsumedGrams, bag.WrittenOffGrams, bag.Version).Should().Be((consumed, writtenOff, version));
    }
}
