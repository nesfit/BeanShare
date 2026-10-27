using BeanShare.Application.Tests.TestHelpers;
using System.Linq;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Consumption;

public sealed class PresetConsumptionTests
{
    private readonly IPresetRecipeRepository _presetRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly RecordConsumptionFromPresetCommandHandler _handler;

    public PresetConsumptionTests()
    {
        _presetRepository = Substitute.For<IPresetRecipeRepository>();
        _spaceRepository = Substitute.For<ISpaceRepository>();
        _consumptionRepository = Substitute.For<IConsumptionRepository>();
        _coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        _userContext = Substitute.For<IUserContext>();
        _clock = Substitute.For<IClock>();

        // The handler always loads the space, for the ledger-start and membership checks: no ledger start is set here,
        // and the caller is the space's admin.
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Space.Create(SpaceId.New(), "Lab", Currency.USD, _userContext.CurrentUserId, new InviteCode("CAFE23"), _clock));

        _handler = new RecordConsumptionFromPresetCommandHandler(
            _presetRepository,
            _spaceRepository,
            _coffeeStockRepository,
            _consumptionRepository,
            _userContext,
            _clock, TestUsers.ActiveRepository());
    }

    [Fact]
    public async Task Should_Record_Consumption_Using_Preset_Values()
    {
        var spaceId = SpaceId.New();
        var userId = UserId.New();
        var presetId = new PresetRecipeId(Guid.NewGuid());

        var preset = PresetRecipe.Create(
            userId,
            spaceId,
            "Espresso Shot",
            "Espresso",
            "Machine",
            Weight.FromGrams(18),
            DateTime.UtcNow,
            null,
            true);

        var now = DateTime.UtcNow;
        _userContext.CurrentUserId.Returns(userId);
        _clock.UtcNow.Returns(now);

        var coffeeStock = CoffeeStock.Create(spaceId, _clock);
        var product = CoffeeProduct.Create("Espresso Shot", "Espresso Shot", Domain.ValueObjects.CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(10, "USD"), "Test Vendor", userId, now.AddDays(-1), _clock);
        _presetRepository.GetByIdAsync(presetId, default).Returns(preset);
        _coffeeStockRepository.GetBySpaceIdAsync(spaceId, default).Returns(coffeeStock);

        var command = new RecordConsumptionFromPresetCommand(
            spaceId.Value,
            presetId.Value,
            null,
            now);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(because: result.IsFailure ? $"Error: {string.Join(", ", result.Errors.Select(e => e.Message))}" : "");
        await _consumptionRepository.Received().AddAsync(
            Arg.Is<ConsumptionEntry>(e => e.Quantity.Grams == 18),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Use_Custom_Quantity_When_Provided()
    {
        var spaceId = SpaceId.New();
        var userId = UserId.New();
        var presetId = new PresetRecipeId(Guid.NewGuid());
        var customGrams = 36m;

        var preset = PresetRecipe.Create(
            userId,
            spaceId,
            "Single Espresso",
            "Espresso",
            "Machine",
            Weight.FromGrams(18),
            DateTime.UtcNow,
            null,
            true);

        var now = DateTime.UtcNow;
        _userContext.CurrentUserId.Returns(userId);
        _clock.UtcNow.Returns(now);

        var coffeeStock = CoffeeStock.Create(spaceId, _clock);
        var product = CoffeeProduct.Create("Single Espresso", "Single Espresso", Domain.ValueObjects.CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(10, "USD"), "Test Vendor", userId, now.AddDays(-1), _clock);
        _presetRepository.GetByIdAsync(presetId, default).Returns(preset);
        _coffeeStockRepository.GetBySpaceIdAsync(spaceId, default).Returns(coffeeStock);

        var command = new RecordConsumptionFromPresetCommand(
            spaceId.Value,
            presetId.Value,
            customGrams,
            now);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(because: result.IsFailure ? $"Error: {string.Join(", ", result.Errors.Select(e => e.Message))}" : "");
        await _consumptionRepository.Received().AddAsync(
            Arg.Is<ConsumptionEntry>(e => e.Quantity.Grams == customGrams),
            Arg.Any<CancellationToken>());
    }
}
