using BeanShare.Application.Common;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using SettlementAggregate = BeanShare.Domain.Aggregates.Settlement.Settlement;

namespace BeanShare.Application.Tests.Features.Settlement;

// T-GEN-01..09: guards, lines, inclusion and auto-confirm of ledger generation.
public sealed class LedgerSettlementGenerationTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan31 = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb1 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _alice;
    private readonly UserId _bob;

    public LedgerSettlementGenerationTests()
    {
        _alice = _world.AddMember("Alice");
        _bob = _world.AddMember("Bob");
        _world.Space.SetTreasurer(_world.Admin);
    }

    // T-GEN-01 (G4)
    [Fact]
    public async Task WithoutATreasurer_GenerationFailsWithTreasurerRequired()
    {
        _world.Space.SetTreasurer(null);
        var period = _world.AddPeriod("January", Jan1, Jan31);

        var result = await _world.GenerateAsync(period);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.TreasurerRequired);
        _world.Settlements.Should().BeEmpty();
    }

    [Fact]
    public async Task WithAnInactiveTreasurer_GenerationFailsWithTreasurerRequired()
    {
        _world.UserOf(_world.Admin).Deactivate(_world.Clock.UtcNow);
        var period = _world.AddPeriod("January", Jan1, Jan31);

        var result = await _world.GenerateAsync(period);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.TreasurerRequired);
    }

    // T-GEN-02 (G2, G3)
    [Theory]
    [InlineData(BillingState.Draft)]
    [InlineData(BillingState.Open)]
    [InlineData(BillingState.Closed)]
    public async Task AnEarlierUnsettledPeriod_BlocksGeneration(BillingState earlierState)
    {
        _world.AddPeriod("January", Jan1, Jan31, earlierState);
        var february = _world.AddPeriod("February", Feb1, Feb28);

        var result = await _world.GenerateAsync(february);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.SettlementOrder);
        result.Errors[0].Message.Should().Contain("January");
    }

    [Fact]
    public async Task ALaterSettledPeriod_BlocksGeneration()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31);
        _world.AddPeriod("February", Feb1, Feb28, BillingState.Settled);

        var result = await _world.GenerateAsync(january);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.SettlementOrder);
    }

    // T-GEN-03 (B3, B8, ADV-7)
    [Fact]
    public async Task BalanceOnlyMembersAndNonDrinkingBuyersGetLines()
    {
        var period = _world.AddPeriod("January", Jan1, Jan31);
        var bag = _world.AddBag(1000m, 500m, Jan1, paidBy: _bob);
        _world.Drink(_world.Admin, 10m, Jan1.AddDays(3), bag);
        _world.AddEntry(_alice, -538m, Jan1);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        var alice = result.Value.Lines.Single(l => l.UserId == _alice.Value);
        alice.Adjustments.Should().Be(-538m);
        alice.NetBalance.Should().Be(-538m);
        alice.TotalCoffeeGrams.Should().Be(0m);
        var bob = result.Value.Lines.Single(l => l.UserId == _bob.Value);
        bob.Credit.Should().Be(5.00m);
        bob.NetBalance.Should().Be(5.00m);
        result.Value.Lines.Single(l => l.IsTreasurer).NetBalance.Should().Be(538m - 5.00m);
        result.Value.Lines.Sum(l => l.NetBalance).Should().Be(0m);
    }

    [Fact]
    public async Task ASettlementWithNoLines_CompletesImmediately()
    {
        var period = _world.AddPeriod("January", Jan1, Jan31);

        var result = await _world.GenerateAsync(period);

        result.Value.Lines.Should().BeEmpty();
        _world.SettlementOf(period).Status.Should().Be(SettlementStatus.Completed);
    }

    // T-GEN-04
    [Fact]
    public async Task CupsStampedToThePeriodAndUnstampedCupsUpToItsEnd_AreIncludedAndStamped()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31);
        var later = _world.AddPeriod("February", Feb1, Feb28, BillingState.Open);
        var bag = _world.AddBag(1000m, 500m, Jan1);
        var stampedLate = _world.Drink(_alice, 10m, Feb1.AddDays(2), bag);
        stampedLate.AssignToBillingPeriod(january.Id);
        var unstamped = _world.Drink(_alice, 10m, Jan1.AddDays(5), bag);
        var stampedElsewhere = _world.Drink(_bob, 10m, Jan1.AddDays(6), bag);
        stampedElsewhere.AssignToBillingPeriod(later.Id);
        var afterEnd = _world.Drink(_bob, 10m, Feb1.AddDays(3), bag);

        var result = await _world.GenerateAsync(january);

        result.Value.Lines.Single(l => l.UserId == _alice.Value).TotalCoffeeGrams.Should().Be(20m);
        result.Value.Lines.Should().NotContain(l => l.UserId == _bob.Value);
        unstamped.BillingPeriodId.Should().Be(january.Id);
        stampedLate.BillingPeriodId.Should().Be(january.Id);
        stampedElsewhere.BillingPeriodId.Should().Be(later.Id);
        afterEnd.BillingPeriodId.Should().BeNull();
    }

    // T-GEN-05 (I10)
    [Fact]
    public async Task ACupBeforeTheLedgerStart_IsNeverBilled()
    {
        _world.Space.SetLedgerStartDate(Jan1.AddDays(10), hasLedgerSettlement: false);
        var period = _world.AddPeriod("January", Jan1, Jan31);
        var bag = _world.AddBag(1000m, 500m, Jan1);
        var before = _world.Drink(_alice, 10m, Jan1.AddDays(2), bag);
        _world.Drink(_alice, 10m, Jan1.AddDays(12), bag);

        var result = await _world.GenerateAsync(period);

        result.Value.Lines.Single(l => l.UserId == _alice.Value).TotalCoffeeGrams.Should().Be(10m);
        before.BillingPeriodId.Should().BeNull();
    }

    // T-GEN-06 (B7)
    [Fact]
    public async Task ACupBackDatedIntoASettledRange_IsBilledByTheNextSettlementExactlyOnce()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31);
        var february = _world.AddPeriod("February", Feb1, Feb28);
        var bag = _world.AddBag(1000m, 500m, Jan1);
        _world.Drink(_alice, 10m, Jan1.AddDays(3), bag);
        (await _world.GenerateAsync(january)).IsSuccess.Should().BeTrue();

        var backDated = _world.Drink(_bob, 10m, Jan1.AddDays(4), bag);
        var feb = await _world.GenerateAsync(february);

        feb.Value.Lines.Single(l => l.UserId == _bob.Value).AmountDue.Should().Be(5.00m);
        backDated.BillingPeriodId.Should().Be(february.Id);
        _world.SettlementOf(january).Lines.Should().NotContain(l => l.UserId == _bob);
    }

    // T-GEN-07 (ADV-1): legacy-billed cups carry their period stamp (backfill), so the ledger skips them.
    [Fact]
    public async Task CupsBilledByALegacySettlement_AreNotBilledAgain()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31, BillingState.Settled);
        var legacy = SettlementAggregate.Create(_world.Space.Id, january.Id, "CZK", _world.Admin, _world.Clock);
        legacy.AddLine(_alice, 10m, 5.00m, _world.Clock);
        legacy.FinalizeGeneration(_world.Clock);
        _world.Settlements.Add(legacy);
        var bag = _world.AddBag(1000m, 500m, Jan1);
        _world.Drink(_alice, 10m, Jan1.AddDays(3), bag).AssignToBillingPeriod(january.Id);
        _world.Drink(_alice, 10m, Feb1.AddDays(3), bag);
        var february = _world.AddPeriod("February", Feb1, Feb28);

        var result = await _world.GenerateAsync(february);

        result.Value.Lines.Single(l => l.UserId == _alice.Value).TotalCoffeeGrams.Should().Be(10m);
        result.Value.Lines.Single(l => l.UserId == _alice.Value).CarriedIn.Should().Be(0m, "legacy lines are never carried");
    }

    // T-GEN-08 (I5)
    [Fact]
    public async Task EntriesUpToTheEnd_AreIncludedOnceAndLocked()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31);
        var february = _world.AddPeriod("February", Feb1, Feb28);
        var opening = _world.AddEntry(_alice, -538m, Jan1);
        var later = _world.AddEntry(_bob, 120m, Feb1.AddDays(1), kind: BalanceEntryKind.Correction, note: "refund");

        var jan = await _world.GenerateAsync(january);
        var feb = await _world.GenerateAsync(february);

        jan.Value.Lines.Single(l => l.UserId == _alice.Value).Adjustments.Should().Be(-538m);
        jan.Value.Lines.Should().NotContain(l => l.UserId == _bob.Value);
        opening.IsLocked.Should().BeTrue();
        opening.IncludedInSettlementId.Should().Be(_world.SettlementOf(january).Id);

        feb.Value.Lines.Single(l => l.UserId == _bob.Value).Adjustments.Should().Be(120m);
        feb.Value.Lines.Single(l => l.UserId == _alice.Value).Adjustments.Should().Be(0m, "an included entry is never counted again");
        later.IncludedInSettlementId.Should().Be(_world.SettlementOf(february).Id);
    }

    // T-GEN-09 (D5, B9, T6)
    [Fact]
    public async Task OnlyAZeroNetAutoConfirms_ADebtorWithoutCupsStaysOpen_AndTheTreasurerNever()
    {
        var period = _world.AddPeriod("January", Jan1, Jan31);
        var bag = _world.AddBag(1000m, 500m, Jan1, paidBy: _bob);
        _world.Drink(_bob, 10m, Jan1.AddDays(2), bag);
        _world.AddEntry(_alice, -538m, Jan1);

        await _world.GenerateAsync(period);
        var settlement = _world.SettlementOf(period);

        settlement.GetLineForUser(_bob)!.Resolution.Should().Be(LineResolution.Confirmed, "credit equals cost");
        settlement.GetLineForUser(_alice)!.Resolution.Should().Be(LineResolution.Open);
        settlement.GetLineForUser(_world.Admin)!.Resolution.Should().Be(LineResolution.Open);
        settlement.Status.Should().Be(SettlementStatus.AwaitingConfirmation);
    }

    [Fact]
    public async Task WhenEveryOtherLineNetsToZero_TheTreasurerResolvesAtGenerationAndItCompletes()
    {
        var period = _world.AddPeriod("January", Jan1, Jan31);
        var bag = _world.AddBag(1000m, 500m, Jan1, paidBy: _bob);
        _world.Drink(_bob, 10m, Jan1.AddDays(2), bag);

        await _world.GenerateAsync(period);
        var settlement = _world.SettlementOf(period);

        settlement.GetLineForUser(_world.Admin)!.Resolution.Should().Be(LineResolution.Confirmed);
        settlement.Status.Should().Be(SettlementStatus.Completed);
    }

    [Fact]
    public async Task WhenOnlyTheTreasurerHasALine_ItCompletesAtGeneration()
    {
        var period = _world.AddPeriod("January", Jan1, Jan31);
        var bag = _world.AddBag(1000m, 500m, Jan1);
        _world.Drink(_world.Admin, 10m, Jan1.AddDays(2), bag);

        var result = await _world.GenerateAsync(period);

        result.Value.Lines.Should().ContainSingle(l => l.IsTreasurer);
        _world.SettlementOf(period).Status.Should().Be(SettlementStatus.Completed);
    }

    [Fact]
    public async Task GenerationBumpsTheSpaceLedgerVersionAndNumbersSettlements()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31);
        var february = _world.AddPeriod("February", Feb1, Feb28);
        var before = _world.Space.LedgerVersion;

        await _world.GenerateAsync(january);
        await _world.GenerateAsync(february);

        _world.SettlementOf(january).Sequence.Should().Be(1);
        _world.SettlementOf(february).Sequence.Should().Be(2);
        _world.Space.LedgerVersion.Should().BeGreaterThan(before);
    }
}
