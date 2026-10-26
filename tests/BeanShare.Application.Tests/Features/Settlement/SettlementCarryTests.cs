using BeanShare.Application.Common;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Application.Tests.Features.Settlement;

// T-GEN-10: unresolved lines carry into the next settlement exactly once (D6, I6).
public sealed class SettlementCarryTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan31 = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb1 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mar1 = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mar5 = new(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _alice;
    private readonly UserId _bob;

    public SettlementCarryTests()
    {
        _alice = _world.AddMember("Alice");
        _bob = _world.AddMember("Bob");
        _world.Space.SetTreasurer(_world.Admin);
        _world.AddEntry(_alice, -538m, Jan1);
        _world.AddEntry(_bob, 120m, Jan1);
    }

    [Fact]
    public async Task OpenLinesCarry_TheTreasurerTakesTheCounterCarry_AndConfirmedLinesDoNot()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31);
        var february = _world.AddPeriod("February", Feb1, Feb28);
        await _world.GenerateAsync(january);
        var s1 = _world.SettlementOf(january);
        (await _world.ConfirmAsync(s1, _alice)).IsSuccess.Should().BeTrue();

        var s2Dto = await _world.GenerateAsync(february);
        var s2 = _world.SettlementOf(february);

        s1.SupersededBySettlementId.Should().Be(s2.Id);
        s1.GetLineForUser(_bob)!.Resolution.Should().Be(LineResolution.Carried);
        s1.GetLineForUser(_bob)!.CarriedOutAmount.Should().Be(120m);
        s1.GetLineForUser(_bob)!.CarriedToSettlementId.Should().Be(s2.Id);
        s1.GetLineForUser(_world.Admin)!.Resolution.Should().Be(LineResolution.Carried);
        s1.GetLineForUser(_world.Admin)!.CarriedOutAmount.Should().Be(-120m);
        s1.GetLineForUser(_alice)!.Resolution.Should().Be(LineResolution.Confirmed);

        s2Dto.Value.Lines.Single(l => l.UserId == _bob.Value).CarriedIn.Should().Be(120m);
        s2Dto.Value.Lines.Single(l => l.UserId == _world.Admin.Value).CarriedIn.Should().Be(-120m);
        s2Dto.Value.Lines.Should().NotContain(l => l.UserId == _alice.Value);
        s2.Lines.Should().OnlyContain(l => l.CarriedFromSettlementId == s1.Id);
    }

    [Fact]
    public async Task ConfirmingACarriedLine_FailsWithLineCarried()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31);
        var february = _world.AddPeriod("February", Feb1, Feb28);
        await _world.GenerateAsync(january);
        await _world.GenerateAsync(february);

        var result = await _world.ConfirmAsync(_world.SettlementOf(january), _bob);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.LineCarried);
    }

    [Fact]
    public async Task ASettlementIsCarriedOnlyOnce_TheNextGenerationCarriesFromTheLatest()
    {
        var january = _world.AddPeriod("January", Jan1, Jan31);
        var february = _world.AddPeriod("February", Feb1, Feb28);
        var march = _world.AddPeriod("March", Mar1, Mar5);
        await _world.GenerateAsync(january);
        await _world.GenerateAsync(february);
        var s1 = _world.SettlementOf(january);

        var carryAgain = () => s1.CarryForwardTo(SettlementId.New(), _world.Clock);
        carryAgain.Should().Throw<InvalidOperationException>();

        var s3 = await _world.GenerateAsync(march);

        var s2 = _world.SettlementOf(february);
        s2.SupersededBySettlementId.Should().Be(_world.SettlementOf(march).Id);
        s3.Value.Lines.Single(l => l.UserId == _alice.Value).CarriedIn.Should().Be(-538m);
        s3.Value.Lines.Single(l => l.UserId == _bob.Value).CarriedIn.Should().Be(120m);
        s3.Value.Lines.Sum(l => l.NetBalance).Should().Be(0m);
    }
}
