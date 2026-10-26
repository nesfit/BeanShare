using BeanShare.Application.Common;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Entities;
using FluentAssertions;

namespace BeanShare.Application.Tests.Features.Consumption;

/// <summary>
/// B22 on the server: a cup before the ledger start C is never billed, so it can be neither deleted (which would hand
/// its grams back to the bag) nor re-dated across C, the same rule the cup lists apply to their Edit and Delete.
/// </summary>
public sealed class LedgerStartCupCommandTests
{
    private static readonly DateTime C = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly LedgerWorld _world = new();

    public LedgerStartCupCommandTests() => _world.Space.SetLedgerStartDate(C, hasLedgerSettlement: false);

    [Fact]
    public async Task DeletingACupBeforeTheLedgerStart_IsRefused_AndTheBagKeepsItsGrams()
    {
        var bag = _world.AddBag(1000m, 500m, C.AddDays(-20));
        var cup = _world.Drink(_world.Admin, 18m, C.AddDays(-1), bag);
        var remaining = bag.RemainingGrams.Grams;

        var result = await DeleteAsync(cup);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.BeforeLedgerStart);
        _world.Cups.Should().Contain(cup);
        bag.RemainingGrams.Grams.Should().Be(remaining);
    }

    [Fact]
    public async Task DeletingACupOnTheLedgerStart_StillWorks()
    {
        var bag = _world.AddBag(1000m, 500m, C.AddDays(-20));
        var cup = _world.Drink(_world.Admin, 18m, C, bag);

        (await DeleteAsync(cup)).IsSuccess.Should().BeTrue();
        _world.Cups.Should().NotContain(cup);
    }

    [Fact]
    public async Task ReDatingACupBeforeTheLedgerStart_PastIt_IsRefused()
    {
        var bag = _world.AddBag(1000m, 500m, C.AddDays(-20));
        var cup = _world.Drink(_world.Admin, 18m, C.AddDays(-1), bag);

        var result = await UpdateAsync(cup, C.AddDays(2));

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.BeforeLedgerStart);
        cup.ConsumedAt.Should().Be(C.AddDays(-1));
    }

    [Fact]
    public async Task ReDatingACupAfterTheLedgerStart_BeforeIt_IsRefused()
    {
        var bag = _world.AddBag(1000m, 500m, C.AddDays(-20));
        var cup = _world.Drink(_world.Admin, 18m, C.AddDays(2), bag);

        var result = await UpdateAsync(cup, C.AddDays(-1));

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.BeforeLedgerStart);
        cup.ConsumedAt.Should().Be(C.AddDays(2));
    }

    [Fact]
    public async Task EditingACupAfterTheLedgerStart_StillWorks()
    {
        var bag = _world.AddBag(1000m, 500m, C.AddDays(-20));
        var cup = _world.Drink(_world.Admin, 18m, C.AddDays(2), bag);

        (await UpdateAsync(cup, C.AddDays(3))).IsSuccess.Should().BeTrue();
        cup.ConsumedAt.Should().Be(C.AddDays(3));
    }

    private Task<Result<bool>> DeleteAsync(ConsumptionEntry cup) =>
        new DeleteConsumptionCommandHandler(_world.ConsumptionRepository, _world.BillingPeriodRepository,
                _world.CoffeeStockRepository, _world.SpaceRepository, _world.Clock)
            .Handle(new DeleteConsumptionCommand(cup.Id.Value, _world.Space.Id.Value), default);

    private Task<Result<BeanShare.Application.Features.Consumption.Dtos.ConsumptionEntryDto>> UpdateAsync(ConsumptionEntry cup, DateTime consumedAt) =>
        new UpdateConsumptionCommandHandler(_world.ConsumptionRepository, _world.BillingPeriodRepository,
                _world.CoffeeStockRepository, _world.SpaceRepository, _world.Clock)
            .Handle(new UpdateConsumptionCommand(cup.Id.Value, _world.Space.Id.Value, "Alfa", "Roaster", "Filter",
                cup.Quantity.Grams, consumedAt), default);
}
