using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using MapsterMapper;
using NSubstitute;
using Xunit;

namespace BeanShare.Application.Tests.Features;

public class CoffeeStockCommandTests
{
    [Fact]
    public async Task AddStockPurchase_WithValidAdmin_ShouldSucceed()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var mapper = Substitute.For<IMapper>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(adminId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), TestClock.Instance);
        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>())
            .Returns((CoffeeStock?)null);

        var handler = new AddStockPurchaseCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock, mapper);

        var command = new AddStockPurchaseCommand(
            spaceId.Value,
            "Premium Blend",
            "Blue Mountain",
            "Espresso",
            1000m,
            25.99m,
            "USD",
            "Local Roasters",
            clock.UtcNow.AddDays(-1));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await coffeeStockRepository.Received(1).AddAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
        await coffeeStockRepository.DidNotReceive().UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddStockPurchase_WithNonAdmin_ShouldReturnError()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var mapper = Substitute.For<IMapper>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());
        var memberId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(memberId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), TestClock.Instance);
        space.Join(memberId, TestClock.Instance);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var handler = new AddStockPurchaseCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock, mapper);

        var command = new AddStockPurchaseCommand(
            spaceId.Value,
            "Premium Blend",
            "Blue Mountain",
            "Espresso",
            1000m,
            25.99m,
            "USD",
            "Local Roasters",
            clock.UtcNow.AddDays(-1));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.Contains("INSUFFICIENT_PRIVILEGES"));
        await coffeeStockRepository.DidNotReceive().AddAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddStockPurchase_WithInvalidSpaceId_ShouldReturnError()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var mapper = Substitute.For<IMapper>();
        var clock = TestClock.Instance;

        var invalidSpaceId = Guid.NewGuid();
        var userId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(userId);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((Domain.Aggregates.Space.Space?)null);

        var handler = new AddStockPurchaseCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock, mapper);

        var command = new AddStockPurchaseCommand(
            invalidSpaceId,
            "Premium Blend",
            "Blue Mountain",
            "Espresso",
            1000m,
            25.99m,
            "USD",
            "Local Roasters",
            clock.UtcNow.AddDays(-1));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.Contains("SPACE_NOT_FOUND"));
        await coffeeStockRepository.DidNotReceive().AddAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddStockPurchase_WithExistingStock_ShouldUpdateStockLevel()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var mapper = Substitute.For<IMapper>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(adminId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), TestClock.Instance);
        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var existingStock = CoffeeStock.Create(spaceId, TestClock.Instance);
        coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>())
            .Returns(existingStock);

        var handler = new AddStockPurchaseCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock, mapper);

        var command = new AddStockPurchaseCommand(
            spaceId.Value,
            "Premium Blend",
            "Blue Mountain",
            "Espresso",
            1000m,
            25.99m,
            "USD",
            "Local Roasters",
            clock.UtcNow.AddDays(-1));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await coffeeStockRepository.DidNotReceive().AddAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
        await coffeeStockRepository.Received(1).UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
        existingStock.Purchases.Should().HaveCount(1);
        existingStock.StockLevels.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddStockPurchase_WithInvalidProductData_ShouldReturnValidationError()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var mapper = Substitute.For<IMapper>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(adminId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), TestClock.Instance);
        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>())
            .Returns((CoffeeStock?)null);

        var handler = new AddStockPurchaseCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock, mapper);

        var command = new AddStockPurchaseCommand(
            spaceId.Value,
            "", // Invalid empty product name
            "Blue Mountain",
            "Espresso",
            1000m,
            25.99m,
            "USD",
            "Local Roasters",
            clock.UtcNow.AddDays(-1));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.Contains("VALIDATION_ERROR"));
        await coffeeStockRepository.DidNotReceive().AddAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteStockPurchase_WithValidAdmin_ShouldRemovePurchaseAndUpdateStockLevel()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(adminId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), TestClock.Instance);
        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var coffeeStock = CoffeeStock.Create(spaceId, TestClock.Instance);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(25.99m, "USD"), "Vendor", adminId, clock.UtcNow.AddDays(-1), clock);
        var purchaseId = coffeeStock.Purchases.First().Id;

        coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>())
            .Returns(coffeeStock);

        var handler = new DeleteStockPurchaseCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock);

        var command = new DeleteStockPurchaseCommand(spaceId.Value, purchaseId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        coffeeStock.Purchases.Should().BeEmpty();
        coffeeStock.StockLevels.Should().BeEmpty();
        await coffeeStockRepository.Received(1).UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteStockPurchase_WithNonAdmin_ShouldReturnError()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());
        var memberId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(memberId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), TestClock.Instance);
        space.Join(memberId, TestClock.Instance);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var handler = new DeleteStockPurchaseCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock);

        var command = new DeleteStockPurchaseCommand(spaceId.Value, Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.Contains("INSUFFICIENT_PRIVILEGES"));
        await coffeeStockRepository.DidNotReceive().UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteStockPurchase_WithNonexistentSpace_ShouldReturnError()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = TestClock.Instance;

        userContext.CurrentUserId.Returns(new UserId(Guid.NewGuid()));

        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((Domain.Aggregates.Space.Space?)null);

        var handler = new DeleteStockPurchaseCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock);

        var command = new DeleteStockPurchaseCommand(Guid.NewGuid(), Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.Contains("SPACE_NOT_FOUND"));
        await coffeeStockRepository.DidNotReceive().UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteStockPurchase_WithNonexistentPurchase_ShouldReturnError()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(adminId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), TestClock.Instance);
        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var coffeeStock = CoffeeStock.Create(spaceId, TestClock.Instance);
        coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>())
            .Returns(coffeeStock);

        var handler = new DeleteStockPurchaseCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock);

        var command = new DeleteStockPurchaseCommand(spaceId.Value, Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.Contains("DOMAIN_ERROR"));
        await coffeeStockRepository.DidNotReceive().UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCurrentlyUsed_WithValidAdmin_ShouldSetStockLevel()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(adminId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), clock);
        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var coffeeStock = CoffeeStock.Create(spaceId, clock);
        var product = CoffeeProduct.Create("Panama Geisha", "Elida Estate", CoffeeType.Filter);
        coffeeStock.AddPurchase(product, Weight.FromGrams(250), Money.Create(45m, "USD"), "Auction Lot", adminId, clock.UtcNow.AddDays(-1), clock);
        var stockLevelId = coffeeStock.StockLevels.First().Id;

        coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>())
            .Returns(coffeeStock);

        var handler = new SetCurrentlyUsedStockLevelCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock);

        var command = new SetCurrentlyUsedStockLevelCommand(spaceId.Value, stockLevelId);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        coffeeStock.StockLevels.First().IsCurrentlyUsed.Should().BeTrue();
        await coffeeStockRepository.Received(1).UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCurrentlyUsed_WithNullStockLevelId_ShouldBeNoOp()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(adminId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), clock);
        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var coffeeStock = CoffeeStock.Create(spaceId, clock);
        var product = CoffeeProduct.Create("Brazilian Santos", "Daterra", CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(20m, "USD"), "Importer", adminId, clock.UtcNow.AddDays(-1), clock);
        coffeeStock.OpenBag(coffeeStock.Purchases.First().Id, clock);

        coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>())
            .Returns(coffeeStock);

        var handler = new SetCurrentlyUsedStockLevelCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock);

        var command = new SetCurrentlyUsedStockLevelCommand(spaceId.Value, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        coffeeStock.StockLevels.Any(sl => sl.IsCurrentlyUsed).Should().BeTrue(); // null = no-op; the open bag stays open
        await coffeeStockRepository.Received(1).UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCurrentlyUsed_WithNonAdmin_ShouldReturnError()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());
        var memberId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(memberId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), clock);
        space.Join(memberId, clock);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var handler = new SetCurrentlyUsedStockLevelCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock);

        var command = new SetCurrentlyUsedStockLevelCommand(spaceId.Value, Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.Contains("INSUFFICIENT_PRIVILEGES"));
        await coffeeStockRepository.DidNotReceive().UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCurrentlyUsed_WithNonexistentSpace_ShouldReturnError()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = TestClock.Instance;

        userContext.CurrentUserId.Returns(new UserId(Guid.NewGuid()));

        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((Domain.Aggregates.Space.Space?)null);

        var handler = new SetCurrentlyUsedStockLevelCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock);

        var command = new SetCurrentlyUsedStockLevelCommand(Guid.NewGuid(), Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.Contains("SPACE_NOT_FOUND"));
        await coffeeStockRepository.DidNotReceive().UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCurrentlyUsed_WithNonexistentStockLevel_ShouldReturnError()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(adminId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), clock);
        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var coffeeStock = CoffeeStock.Create(spaceId, clock);
        coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>())
            .Returns(coffeeStock);

        var handler = new SetCurrentlyUsedStockLevelCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock);

        var command = new SetCurrentlyUsedStockLevelCommand(spaceId.Value, Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.Contains("STOCK_LEVEL_NOT_FOUND"));
        await coffeeStockRepository.DidNotReceive().UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCurrentlyUsed_WithNoCoffeeStockForSpace_ShouldReturnStockNotFound()
    {
        var coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = TestClock.Instance;

        var spaceId = new SpaceId(Guid.NewGuid());
        var adminId = new UserId(Guid.NewGuid());

        userContext.CurrentUserId.Returns(adminId);

        var space = Domain.Aggregates.Space.Space.Create(spaceId, "Test Space", Currency.USD, adminId, new InviteCode("CAFE23"), clock);
        spaceRepository.GetSingleBySpecAsync(Arg.Any<SpaceByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(space);

        coffeeStockRepository.GetBySpaceIdAsync(spaceId, Arg.Any<CancellationToken>())
            .Returns((CoffeeStock?)null);

        var handler = new SetCurrentlyUsedStockLevelCommandHandler(coffeeStockRepository, spaceRepository, userContext, clock);

        var command = new SetCurrentlyUsedStockLevelCommand(spaceId.Value, Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.Contains("STOCK_NOT_FOUND"));
        await coffeeStockRepository.DidNotReceive().UpdateAsync(Arg.Any<CoffeeStock>(), Arg.Any<CancellationToken>());
    }
}