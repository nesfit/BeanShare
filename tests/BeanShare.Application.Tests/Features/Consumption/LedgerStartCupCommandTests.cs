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

    // O7: "delete it instead" only where a delete would go through; a frozen share gets the refusal a delete gets.
    [Fact]
    public async Task EditingAShareInAClosedPeriod_IsRefusedForThePeriod_NotAsAShare()
    {
        var closed = _world.AddPeriod("October", C, C.AddDays(14));
        var share = Share(C.AddDays(3));
        share.AssignToBillingPeriod(closed.Id);

        var result = await UpdateAsync(share, share.ConsumedAt);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.InvalidBillingPeriodState);
        (await DeleteAsync(share)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.InvalidBillingPeriodState);
    }

    [Fact]
    public async Task EditingAShareBeforeTheLedgerStart_IsRefusedForTheLedgerStart()
    {
        var share = Share(C.AddDays(-3));

        (await UpdateAsync(share, share.ConsumedAt)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.BeforeLedgerStart);
    }

    [Fact]
    public async Task EditingAnOpenShare_SaysDeleteItInstead_AndTheDeleteWorks()
    {
        var share = Share(C.AddDays(3));

        var result = await UpdateAsync(share, share.ConsumedAt);

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("delete it instead");
        (await DeleteAsync(share)).IsSuccess.Should().BeTrue();
    }

    private ConsumptionEntry Share(DateTime at)
    {
        var share = ConsumptionEntry.Create(_world.Space.Id, _world.Admin, LedgerWorld.Product, Domain.ValueObjects.Weight.FromGrams(40m), at,
            _world.Clock, isWriteOff: true);
        _world.Cups.Add(share);
        return share;
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
