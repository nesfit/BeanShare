using System.Globalization;
using FluentAssertions;

namespace BeanShare.Tests.Integration.Scenarios;

// The E2E scenario's own arithmetic, checked without Docker: the pasted texts carry the stated
// totals, and every expected settlement nets to zero.
public sealed class OpeningBalanceScenarioTests
{
    private readonly OpeningBalanceScenario _scenario = new(new DateTime(2026, 10, 26, 15, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void DatesAreRelativeToToday()
    {
        _scenario.D.Should().Be(new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
        _scenario.C.Should().Be(new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc));
        _scenario.AtC(-2).Should().Be(new DateTime(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TheHistoryTexts_CarryFourPeriods_162Cups_And4132()
    {
        var cups = Rows(_scenario.HistoryCups);
        var purchases = Rows(_scenario.HistoryPurchases);

        cups.Select(r => r[0]).Distinct().Should().HaveCount(OpeningBalanceScenario.HistoryPeriods);
        cups.Sum(r => int.Parse(r[4], CultureInfo.InvariantCulture)).Should().Be(OpeningBalanceScenario.HistoryCupsTotal);
        purchases.Sum(r => Amount(r[5])).Should().Be(OpeningBalanceScenario.HistoryCostTotal);
        purchases.Select(r => r[0]).Should().BeSubsetOf(cups.Select(r => r[0]));
        DateTime.Parse(cups.Last()[2], CultureInfo.InvariantCulture).Should().BeBefore(_scenario.C, "every historical End is before C");
    }

    [Fact]
    public void TheOpeningPaste_Has29Rows_SummingToMinus4198()
    {
        var rows = Rows(OpeningBalanceScenario.OpeningPaste, header: false);

        rows.Should().HaveCount(OpeningBalanceScenario.OpeningRows);
        rows.Sum(r => Amount(r[1])).Should().Be(OpeningBalanceScenario.OpeningTotal);
        OpeningBalanceScenario.PlaceholderOpeningTotal.Should().Be(-3780m);
    }

    [Fact]
    public void EveryExpectedSettlement_NetsToZero()
    {
        OpeningBalanceScenario.S1Nets.Values.Sum().Should().Be(0m);
        OpeningBalanceScenario.S1Nets.Should().HaveCount(30);
        OpeningBalanceScenario.S2Nets.Values.Sum().Should().Be(0m);
        OpeningBalanceScenario.S4Nets.Values.Sum().Should().Be(0m);
        OpeningBalanceScenario.S5Nets.Values.Sum().Should().Be(0m);
    }

    [Fact]
    public void TheStep9Lines_AddUp()
    {
        // A: 100 g X + 40 g Y; B: 50 g X; H: 60 g Y. X 0.50 per g paid by H; Y 0.70 per g paid by B.
        var costA = 100m * OpeningBalanceScenario.XPricePerGram + 40m * OpeningBalanceScenario.YPricePerGram;
        var costB = 50m * OpeningBalanceScenario.XPricePerGram;
        var costH = 60m * OpeningBalanceScenario.YPricePerGram;
        var creditH = (100m + 50m) * OpeningBalanceScenario.XPricePerGram;
        var creditB = (40m + 60m) * OpeningBalanceScenario.YPricePerGram;

        (costA, costB, costH, creditH, creditB).Should().Be((78m, 25m, 42m, 75m, 70m));
        (0m - costA - 538m).Should().Be(OpeningBalanceScenario.S1Nets["A"]);
        (creditB - costB + 120m).Should().Be(OpeningBalanceScenario.S1Nets["B"]);
        (creditH - costH + 538m - 120m + 3780m).Should().Be(OpeningBalanceScenario.S1Nets["H"]);
    }

    [Fact]
    public void TheStep16Lines_AddUp()
    {
        var share = OpeningBalanceScenario.YRemainder / 2 * OpeningBalanceScenario.YPricePerGram;
        var p27Cup = 10m * OpeningBalanceScenario.YPricePerGram;

        OpeningBalanceScenario.YRemainder.Should().Be(870m);
        share.Should().Be(304.50m);
        (-share - 14m - (270m + p27Cup)).Should().Be(OpeningBalanceScenario.S4Nets["A"]);
        (p27Cup + 2 * share + 14m).Should().Be(OpeningBalanceScenario.S4Nets["B"]);
        (-share + 220m).Should().Be(OpeningBalanceScenario.S4Nets["H"]);
        (OpeningBalanceScenario.S4Nets["H"] + 630m).Should().Be(OpeningBalanceScenario.S5Nets["H"]);
    }

    private static List<string[]> Rows(string text, bool header = true) =>
        text.Split('\n').Skip(header ? 1 : 0).Select(l => l.Split(';')).ToList();

    private static decimal Amount(string text) => decimal.Parse(text.Replace(',', '.'), CultureInfo.InvariantCulture);
}
