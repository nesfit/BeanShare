using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using NSubstitute;

namespace BeanShare.Application.Tests.Features;

public sealed class ConsumptionCommandTests
{
    private readonly ICoffeeStockRepository _coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
    private readonly IConsumptionRepository _consumptionRepository = Substitute.For<IConsumptionRepository>();
    private readonly ISpaceRepository _spaceRepository = Substitute.For<ISpaceRepository>();
    private readonly IUserContext _userContext = Substitute.For<IUserContext>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly RecordConsumptionCommandHandler _handler;

    public ConsumptionCommandTests()
    {
        // The space is always loaded for the ledger-start check (DS-20); none is set by default.
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Domain.Aggregates.Space.Space>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Domain.Aggregates.Space.Space.Create(SpaceId.New(), "Lab", Currency.USD, UserId.New(), new InviteCode("CAFE23"), _clock));

        _handler = new RecordConsumptionCommandHandler(_coffeeStockRepository, _consumptionRepository, _spaceRepository, _userContext, _clock);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldRecordConsumption()
    {
        var spaceId = SpaceId.New();
        var userId = UserId.New();
        var product = CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso);
        var quantity = Weight.FromGrams(15.0m);
        var consumedAt = DateTime.UtcNow.AddMinutes(-30);
        var now = DateTime.UtcNow;

        var coffeeStock = CoffeeStock.Create(spaceId, _clock);

        _clock.UtcNow.Returns(now);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(25, "USD"), "Test Vendor", userId, now.AddDays(-1), _clock);

        _userContext.CurrentUserId.Returns(userId);
        _clock.UtcNow.Returns(now);
        _coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>()).Returns(coffeeStock);

        var command = new RecordConsumptionCommand(
            spaceId.Value,
            product.Name,
            product.Brand,
            product.Type.ToString(),
            quantity.Grams,
            consumedAt);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.QuantityGrams.Should().Be(15.0m);
        result.Value.RemainingGrams.Should().Be(985.0m);
        result.Value.ConsumedAt.Should().Be(consumedAt);

        await _consumptionRepository.Received(1).AddAsync(Arg.Any<ConsumptionEntry>(), Arg.Any<CancellationToken>());
        await _coffeeStockRepository.Received(1).UpdateAsync(coffeeStock, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNoStock_ShouldReturnStockNotFound()
    {
        var spaceId = SpaceId.New();
        var userId = UserId.New();
        var now = DateTime.UtcNow;
        var consumedAt = now.AddMinutes(-30);

        var command = new RecordConsumptionCommand(
            spaceId.Value,
            "Test Coffee",
            "Test Brand",
            "Espresso",
            15.0m,
            consumedAt);

        _userContext.CurrentUserId.Returns(userId);
        _clock.UtcNow.Returns(now);
        _coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>()).Returns((CoffeeStock?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "STOCK_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WithInvalidCoffeeType_ShouldReturnInvalidType()
    {
        var spaceId = SpaceId.New();
        var userId = UserId.New();
        var command = new RecordConsumptionCommand(
            spaceId.Value,
            "Test Coffee",
            "Test Brand",
            "InvalidType",
            15.0m,
            DateTime.UtcNow.AddMinutes(-30));

        _userContext.CurrentUserId.Returns(userId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "INVALID_COFFEE_TYPE");
    }

    [Fact]
    public async Task Handle_WithFutureTime_ShouldReturnInvalidTime()
    {
        var spaceId = SpaceId.New();
        var userId = UserId.New();
        var futureTime = DateTime.UtcNow.AddMinutes(30);
        var command = new RecordConsumptionCommand(
            spaceId.Value,
            "Test Coffee",
            "Test Brand",
            "Espresso",
            15.0m,
            futureTime);

        _userContext.CurrentUserId.Returns(userId);
        _clock.UtcNow.Returns(DateTime.UtcNow);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "INVALID_CONSUMPTION_TIME");
    }

    [Fact]
    public async Task Handle_WithProductNotInStock_ShouldReturnProductNotFound()
    {
        var spaceId = SpaceId.New();
        var userId = UserId.New();
        var now = DateTime.UtcNow;
        var consumedAt = now.AddMinutes(-30);

        var coffeeStock = CoffeeStock.Create(spaceId, _clock);

        _userContext.CurrentUserId.Returns(userId);
        _clock.UtcNow.Returns(now);
        _coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>()).Returns(coffeeStock);

        var command = new RecordConsumptionCommand(
            spaceId.Value,
            "Nonexistent Coffee",
            "Unknown Brand",
            "Espresso",
            15.0m,
            consumedAt);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "PRODUCT_NOT_FOUND_IN_STOCK");
    }

    [Fact]
    public async Task Handle_WithInsufficientStock_ShouldReturnInsufficientError()
    {
        var spaceId = SpaceId.New();
        var userId = UserId.New();
        var product = CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso);
        var now = DateTime.UtcNow;
        var consumedAt = now.AddMinutes(-30);

        var coffeeStock = CoffeeStock.Create(spaceId, _clock);

        _clock.UtcNow.Returns(now);

        // Add only 10g to stock
        coffeeStock.AddPurchase(product, Weight.FromGrams(10), Money.Create(5, "USD"), "Test Vendor", userId, now.AddDays(-1), _clock);

        _userContext.CurrentUserId.Returns(userId);
        _coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>()).Returns(coffeeStock);

        var command = new RecordConsumptionCommand(
            spaceId.Value,
            product.Name,
            product.Brand,
            product.Type.ToString(),
            15.0m, // Try to consume 15g when only 10g available
            consumedAt);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "INSUFFICIENT_STOCK");
    }
}