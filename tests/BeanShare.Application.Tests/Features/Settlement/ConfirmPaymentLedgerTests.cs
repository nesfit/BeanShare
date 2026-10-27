using BeanShare.Application.Common;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using SettlementAggregate = BeanShare.Domain.Aggregates.Settlement.Settlement;

namespace BeanShare.Application.Tests.Features.Settlement;

// Who may confirm which ledger line, and how the treasurer's line resolves.
public sealed class ConfirmPaymentLedgerTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan31 = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _alice;
    private readonly UserId _bob;
    private readonly UserId _carol;

    public ConfirmPaymentLedgerTests()
    {
        _alice = _world.AddMember("Alice");
        _bob = _world.AddMember("Bob");
        _carol = _world.AddMember("Carol");
        _world.Space.SetTreasurer(_world.Admin);
        _world.AddEntry(_alice, -100m, Jan1);
        _world.AddEntry(_bob, 40m, Jan1);
    }

    private async Task<SettlementAggregate> GenerateJanuaryAsync()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31);
        (await _world.GenerateAsync(january)).IsSuccess.Should().BeTrue();
        return _world.SettlementOf(january);
    }

    [Fact]
    public async Task TheMemberMayConfirmTheirOwnLine()
    {
        var settlement = await GenerateJanuaryAsync();
        _world.Actor = _alice;

        (await _world.ConfirmAsync(settlement, _alice)).IsSuccess.Should().BeTrue();
        settlement.GetLineForUser(_alice)!.Resolution.Should().Be(LineResolution.Confirmed);
    }

    [Fact]
    public async Task AnotherMember_MayNotConfirm()
    {
        var settlement = await GenerateJanuaryAsync();
        _world.Actor = _carol;

        var result = await _world.ConfirmAsync(settlement, _alice);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.InsufficientPrivileges);
    }

    [Fact]
    public async Task TheSettlementsTreasurer_MayConfirmEvenAfterLosingAdminRights()
    {
        var formerTreasurer = _world.AddMember("Former Treasurer", admin: true);
        _world.Space.SetTreasurer(formerTreasurer);
        var settlement = await GenerateJanuaryAsync();
        _world.Space.SetTreasurer(_world.Admin);
        _world.Space.DemoteMember(formerTreasurer, _world.Clock);
        _world.Actor = formerTreasurer;

        (await _world.ConfirmAsync(settlement, _bob)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TheTreasurersOwnLine_IsNotConfirmable()
    {
        var settlement = await GenerateJanuaryAsync();

        var result = await _world.ConfirmAsync(settlement, _world.Admin);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConfirmationFailed);
    }

    [Fact]
    public async Task TheLastConfirmation_ResolvesTheTreasurerAndCompletesTheSettlement()
    {
        var settlement = await GenerateJanuaryAsync();

        (await _world.ConfirmAsync(settlement, _alice)).IsSuccess.Should().BeTrue();
        settlement.Status.Should().Be(SettlementStatus.AwaitingConfirmation);
        settlement.GetLineForUser(_world.Admin)!.Resolution.Should().Be(LineResolution.Open);

        (await _world.ConfirmAsync(settlement, _bob)).IsSuccess.Should().BeTrue();
        settlement.GetLineForUser(_world.Admin)!.Resolution.Should().Be(LineResolution.Confirmed);
        settlement.Status.Should().Be(SettlementStatus.Completed);
        settlement.Version.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task LegacySettlements_KeepTheirRules()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31, BillingState.Settled);
        var legacy = SettlementAggregate.Create(_world.Space.Id, january.Id, "CZK", _world.Admin, _world.Clock);
        legacy.AddLine(_alice, 10m, 5m, _world.Clock);
        legacy.AddLine(_bob, 10m, 5m, _world.Clock);
        legacy.FinalizeGeneration(_world.Clock);
        _world.Settlements.Add(legacy);

        _world.Actor = _alice;
        (await _world.ConfirmAsync(legacy, _alice)).IsSuccess.Should().BeTrue();
        (await _world.ConfirmAsync(legacy, _bob)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(Error.Codes.InsufficientPrivileges);

        _world.Actor = _world.Admin;
        (await _world.ConfirmAsync(legacy, _bob)).IsSuccess.Should().BeTrue();
        legacy.Status.Should().Be(SettlementStatus.Completed);
        legacy.GetLineForUser(_alice)!.Resolution.Should().BeNull();
    }

    // The settlement's treasurer loses the confirm right with their membership.
    [Fact]
    public async Task AFormerTreasurerWhoLeftTheSpace_MayNotConfirm()
    {
        var formerTreasurer = _world.AddMember("Former Treasurer", admin: true);
        _world.Space.SetTreasurer(formerTreasurer);
        var settlement = await GenerateJanuaryAsync();
        _world.Space.SetTreasurer(_world.Admin);
        _world.Space.DemoteMember(formerTreasurer, _world.Clock);
        _world.Space.RemoveMember(formerTreasurer, _world.Clock);
        _world.Actor = formerTreasurer;

        var result = await _world.ConfirmAsync(settlement, _bob);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.InsufficientPrivileges);
    }
}
