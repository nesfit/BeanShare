using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Features.Settlement.Queries.GetSettlementReportData;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Common;

namespace BeanShare.Application.Tests.Features.Settlement;

// With a rounding residual the exported breakdown still adds up to Net on every line,
// the treasurer's included, because Rounding is a column of its own.
public sealed class ReportBreakdownTests
{
    private static readonly DateTime Feb1 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb10 = new(2026, 2, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _treasurer;

    public ReportBreakdownTests()
    {
        _treasurer = _world.AddMember("Tess", admin: true);
        _world.Space.SetTreasurer(_treasurer);

        var alice = _world.AddMember("Alice");
        var bob = _world.AddMember("Bob");
        var carol = _world.AddMember("Carol");

        // Three fallback-priced cups of 33.33 against a 100.00 bag: a residual of 0.01.
        _world.AddBag(30m, 100.00m, Feb10.AddDays(-1), paidBy: bob);
        _world.LegacyCup(alice, 10m, Feb10);
        _world.LegacyCup(carol, 10m, Feb10);
        _world.LegacyCup(_treasurer, 10m, Feb10);
        _world.AddEntry(alice, -5m, Feb10, counterparty: carol);
    }

    [Fact]
    public async Task EveryReportLine_AddsUpToNet_AndOnlyTheTreasurerCarriesRounding()
    {
        var period = _world.AddPeriod("February", Feb1, Feb28);
        (await _world.GenerateAsync(period)).IsSuccess.Should().BeTrue();
        var settlement = _world.SettlementOf(period);

        var report = await new GetSettlementReportDataHandler(
                _world.SettlementRepository, _world.BillingPeriodRepository, _world.SpaceRepository, _world.UserService, _world.UserContext)
            .Handle(new GetSettlementReportDataQuery(settlement.Id), default);

        report.IsSuccess.Should().BeTrue();
        var lines = report.Value.Lines;
        lines.Should().HaveCount(4, "Alice, Bob, Carol and the treasurer");
        lines.Should().AllSatisfy(l =>
            ((l.Credit ?? 0) - l.AmountDue + (l.CarriedIn ?? 0) + (l.Adjustments ?? 0) + l.MergeTransfer + (l.Rounding ?? 0))
                .Should().Be(l.Net!.Value, $"{l.UserName}'s breakdown must add up to the Net"));

        var treasurerLine = lines.Single(l => l.IsTreasurer);
        treasurerLine.UserName.Should().Be("Tess");
        treasurerLine.Rounding.Should().Be(-0.01m);
        lines.Where(l => !l.IsTreasurer).Should().AllSatisfy(l => (l.Rounding ?? 0).Should().Be(0m));
        lines.Sum(l => l.Net!.Value).Should().Be(0m);
        lines.Sum(l => l.MergeTransfer).Should().Be(0m);
        report.Value.Transfers.Should().NotBeNull();
        report.Value.Transfers!.Sum(t => t.ToUserId == _treasurer.Value ? t.Amount : -t.Amount)
            .Should().Be(treasurerLine.Net!.Value, "the treasurer's own net is what they collect minus what they pay out");
    }

    [Fact]
    public async Task EveryUnsettledMemberRow_AddsUpToTheBalance_AndOnlyTheTreasurerCarriesRounding()
    {
        _world.Actor = _world.Admin;

        var summary = await _world.UnsettledAsync();

        summary.Members.Should().NotBeEmpty();
        summary.Members.Should().AllSatisfy(m =>
            (m.Credit - m.Cost + m.Carried + m.Adjustments + m.MergeTransfer + m.Rounding)
                .Should().Be(m.Balance, $"{m.UserName}'s breakdown must add up to the balance"));
        summary.Members.Single(m => m.UserId == _treasurer.Value).Rounding.Should().Be(-0.01m);
        summary.Members.Where(m => m.UserId != _treasurer.Value).Should().AllSatisfy(m => m.Rounding.Should().Be(0m));
        summary.Members.Sum(m => m.Balance).Should().Be(0m);
    }
}
