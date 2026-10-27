using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Application.Tests.Features.Settlement;

// A generated settlement never changes afterwards, and its preview equals what generation stores.
public sealed class SettlementHistoryImmutabilityTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan31 = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb1 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _alice;
    private readonly UserId _bob;

    public SettlementHistoryImmutabilityTests()
    {
        _alice = _world.AddMember("Alice");
        _bob = _world.AddMember("Bob");
        _world.Space.SetTreasurer(_world.Admin);
    }

    [Fact]
    public async Task EditingPurchasesOrEntriesAfterGeneration_DoesNotChangeTheSettlement()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31);
        var bag = _world.AddBag(1000m, 500m, Jan1, paidBy: _bob);
        _world.Drink(_alice, 10m, Jan1.AddDays(3), bag);
        _world.LegacyCup(_alice, 10m, Jan1.AddDays(4));
        _world.AddEntry(_alice, -50m, Jan1);
        await _world.GenerateAsync(january);
        var settlement = _world.SettlementOf(january);
        var before = (await _world.GetSettlementAsync(settlement)).Value;

        _world.Stock.UpdatePurchase(bag.Id, Weight.FromGrams(900m), 900m, Jan1.AddDays(-5), _world.Clock);
        _world.Stock.SetPurchasePaidBy(bag.Id, _alice, _world.Clock);
        _world.AddEntry(_alice, 77m, Feb1, kind: BalanceEntryKind.Correction, note: "later");

        var after = (await _world.GetSettlementAsync(settlement)).Value;

        after.Lines.Should().BeEquivalentTo(before.Lines);
        after.Transfers.Should().BeEquivalentTo(before.Transfers);
        after.TotalAmount.Should().Be(before.TotalAmount);
    }

    [Fact]
    public async Task ThePreview_EqualsTheGeneratedSettlementLineForLine()
    {
        _world.AddEntry(_bob, 30m, Jan1);
        await _world.GenerateAsync(_world.AddPeriod("January", Jan1, Jan31));

        var february = _world.AddPeriod("February", Feb1, Feb28);
        var bag = _world.AddBag(1000m, 500m, Feb1, paidBy: _bob);
        _world.Drink(_alice, 10m, Feb1.AddDays(2), bag);
        _world.LegacyCup(_bob, 10m, Feb1.AddDays(3));
        _world.AddEntry(_alice, -12.5m, Feb1.AddDays(1), kind: BalanceEntryKind.Correction, note: "fix");

        // Neither a purchase after the end nor a cup stamped to a later period may move the preview.
        _world.AddBag(1000m, 999m, Feb28.AddDays(3));
        var march = _world.AddPeriod("March", Feb28.AddDays(1), Feb28.AddDays(5));
        _world.LegacyCup(_alice, 10m, Feb1.AddDays(5)).AssignToBillingPeriod(march.Id);

        var preview = (await _world.PreviewAsync(february)).Value;
        var generated = (await _world.GenerateAsync(february)).Value;

        preview.Lines.Should().BeEquivalentTo(generated.Lines, options => options
            .Excluding(l => l.IsConfirmed)
            .Excluding(l => l.ConfirmedBy)
            .Excluding(l => l.ConfirmedAt)
            .Excluding(l => l.Resolution));
        preview.Transfers.Should().BeEquivalentTo(generated.Transfers);
        preview.TotalAmount.Should().Be(generated.TotalAmount);
    }
}
