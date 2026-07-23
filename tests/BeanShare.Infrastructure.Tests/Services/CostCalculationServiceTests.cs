using BeanShare.Application.Abstractions;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence.Services;

namespace BeanShare.Infrastructure.Tests.Services;

public sealed class CostCalculationServiceTests
{
    private readonly ISpaceRepository _spaceRepository = Substitute.For<ISpaceRepository>();
    private readonly ICoffeeStockRepository _coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly SpaceId _spaceId = SpaceId.New();
    private readonly UserId _userId = new(Guid.NewGuid());

    public CostCalculationServiceTests()
    {
        _clock.UtcNow.Returns(new DateTime(2025, 10, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task CalculateConsumptionCost_WithSubCentPerGramRate_ShouldNotRoundRateBeforeMultiplying()
    {
        // 18.00 USD for 1000 g => 0.018 USD/g. Consuming all 1000 g must cost exactly 18.00,
        // not 20.00 (which is what rounding the per-gram rate to 0.02 would produce).
        var stock = CreateStockWithPurchase(grams: 1000m, cost: 18.00m);
        SetupRepositories(stock);

        var service = new CostCalculationService(_spaceRepository, _coffeeStockRepository);

        var cost = await service.CalculateConsumptionCostAsync(_spaceId, 1000m);

        cost.Should().NotBeNull();
        cost!.Amount.Should().Be(18.00m);
    }

    private CoffeeStock CreateStockWithPurchase(decimal grams, decimal cost)
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso);
        stock.AddPurchase(
            product,
            Weight.FromGrams(grams),
            Money.Create(cost, "USD"),
            "Test Vendor",
            _userId,
            _clock.UtcNow,
            _clock);
        return stock;
    }

    private void SetupRepositories(CoffeeStock stock)
    {
        var space = Space.Create(_spaceId, "Test Space", Currency.USD, _userId, new InviteCode("TESTAB"), _clock);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>()).Returns(space);
        _coffeeStockRepository.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(stock);
    }
}
