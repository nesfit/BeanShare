using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;

namespace BeanShare.Application.Tests.Features.Balances;

// "Paid in Excel" bags in the fallback price and credit.
public sealed class CutoverFallbackTests
{
    private static readonly DateTime C = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _a;
    private readonly UserId _b;
    private readonly UserId _d;
    private readonly UserId _eve;

    public CutoverFallbackTests()
    {
        _a = _world.AddMember("A");
        _b = _world.AddMember("B");
        _d = _world.AddMember("D");
        _eve = _world.AddMember("Eve");
        _world.Space.SetTreasurer(_world.Admin);
        _world.Space.SetLedgerStartDate(C, hasLedgerSettlement: false);
    }

    /// <summary>75 bag-less 10 g cups, dated from C to 07-24.</summary>
    private void BagLessCups()
    {
        for (var i = 0; i < 75; i++)
            _world.LegacyCup(_eve, 10m, C.AddDays(i % 24).AddHours(8));
    }

    private (Purchase X, Purchase Y, Purchase Z) Bags(UserId? payerX = null, UserId? payerY = null, UserId? payerZ = null)
    {
        var x = _world.AddBag(1000m, 600m, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), paidBy: payerX ?? _a);
        var y = _world.AddBag(1000m, 600m, new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc), paidBy: payerY ?? _b);
        var z = _world.AddBag(1000m, 600m, new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc), paidBy: payerZ ?? _d);
        x.SetExcludedFromLedgerCredit(true);
        return (x, y, z);
    }

    private async Task<LedgerSnapshot> SnapshotAsync()
    {
        var period = _world.AddPeriod("Cutover", C, PeriodEnd);
        return await _world.SnapshotService.ForPeriodAsync(_world.Space, period, forUpdate: false, default);
    }

    [Fact]
    public async Task AFlaggedBag_IsLeftOutOfThePrice_AndEarnsNothing()
    {
        Bags();
        BagLessCups();

        var snapshot = await SnapshotAsync();
        var calc = snapshot.Calculation;

        calc.FallbackRate.Should().Be(0.60m);
        calc.LineFor(_eve)!.Cost.Should().Be(450.00m);
        calc.LineFor(_b)!.Credit.Should().Be(225.00m);
        calc.LineFor(_d)!.Credit.Should().Be(225.00m);
        (calc.LineFor(_a)?.Credit ?? 0m).Should().Be(0m);
        calc.Lines.Sum(l => l.Net).Should().Be(0m);
        snapshot.FallbackRateMissing.Should().BeFalse();
    }

    [Fact]
    public async Task ACupStampedToAFlaggedBag_IsPricedAtTheFallbackRate_AndCreditedToTheOthers()
    {
        var (x, _, _) = Bags();
        BagLessCups();
        var stamped = _world.Drink(_eve, 10m, C.AddDays(3), x);
        stamped.CostAmount.Should().Be(6.00m, "the cup keeps its stamped price");

        var calc = (await SnapshotAsync()).Calculation;

        calc.LineFor(_eve)!.Cost.Should().Be(456.00m);
        calc.LineFor(_b)!.Credit.Should().Be(228.00m);
        calc.LineFor(_d)!.Credit.Should().Be(228.00m);
        (calc.LineFor(_a)?.Credit ?? 0m).Should().Be(0m);
        calc.Lines.Sum(l => l.Net).Should().Be(0m);
    }

    [Fact]
    public async Task WithOnePayerForAllBags_OnlyThePerCupPriceIsApproximate()
    {
        var t = _world.Admin;
        var (x, _, _) = Bags(t, t, t);
        BagLessCups();
        _world.Drink(_eve, 10m, C.AddDays(3), x);

        var calc = (await SnapshotAsync()).Calculation;

        calc.LineFor(t)!.Credit.Should().Be(456.00m);
        calc.LineFor(_eve)!.Cost.Should().Be(456.00m);
        calc.Lines.Sum(l => l.Net).Should().Be(0m);
    }

    [Fact]
    public async Task WithEveryPricedBagFlagged_TheFallbackRateIsMissing_UntilOneIsUnflagged()
    {
        var x = _world.AddBag(1000m, 600m, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), paidBy: _a);
        var y = _world.AddBag(1000m, 600m, new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc), paidBy: _b);
        x.SetExcludedFromLedgerCredit(true);
        y.SetExcludedFromLedgerCredit(true);
        BagLessCups();
        var period = _world.AddPeriod("Cutover", C, PeriodEnd);

        var missing = await _world.PreviewAsync(period);
        missing.Value.FallbackRateMissing.Should().BeTrue();
        missing.Value.Lines.Single(l => l.UserId == _eve.Value).AmountDue.Should().Be(0m);

        y.SetExcludedFromLedgerCredit(false);
        var priced = await _world.PreviewAsync(period);
        priced.Value.FallbackRateMissing.Should().BeFalse();
        priced.Value.Lines.Single(l => l.UserId == _eve.Value).AmountDue.Should().Be(450.00m);
    }
}
