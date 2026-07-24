using BeanShare.Application.Abstractions;
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
