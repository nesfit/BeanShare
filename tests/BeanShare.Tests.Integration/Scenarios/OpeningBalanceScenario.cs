using System.Globalization;

namespace BeanShare.Tests.Integration.Scenarios;

/// <summary>
/// The data of the opening-balance E2E scenario (design-tests §12): dates relative to D = today's UTC date
/// with C = D − 70, the pasted texts, and the expected nets of every settlement. Pure, so its arithmetic is
/// checked without Docker (<see cref="OpeningBalanceScenarioTests"/>).
/// </summary>
public sealed class OpeningBalanceScenario
{
    public const int PlaceholderCount = 27;

    public OpeningBalanceScenario(DateTime todayUtc)
    {
        D = DateTime.SpecifyKind(todayUtc.Date, DateTimeKind.Utc);
        C = D.AddDays(-70);
    }

    public DateTime D { get; }
    public DateTime C { get; }

    /// <summary>A moment on day <paramref name="day"/> relative to C, at 09:00 UTC.</summary>
    public DateTime AtC(int day) => C.AddDays(day).AddHours(9);

    public static string PlaceholderName(int nn) => $"Placeholder {nn:00}";

    /// <summary>P01 and P03 have an email; the other placeholders have none.</summary>
    public static string? PlaceholderEmail(int nn) => nn is 1 or 3 ? $"p{nn:00}@example.org" : null;

    public static decimal OpeningAmountOf(int nn) => -10m * nn;

    public string HistoryCups => string.Join('\n',
        "period;start;end;member;cups",
        "2017-03 C306;2017-03-01;2017-03-31;h@example.org;40",
        "2017-03 C306;2017-03-01;2017-03-31;Placeholder 01;12",
        "2019-10-16 C306;2019-09-01;2019-10-16;h@example.org;55",
        "2019-10-16 C308;2019-09-01;2019-10-16;Placeholder 27;30",
        $"Final round;{Day(C.AddDays(-40))};{Day(C.AddDays(-1))};a@example.org;25");

    public string HistoryPurchases => string.Join('\n',
        "period;date;paidBy;description;grams;cost",
        "2017-03 C306;2017-03-02;h@example.org;Alfa 1kg;1000;450,00",
        "2019-10-16 C308;;;PoppyBeans;;3032.00",
        $"Final round;{Day(C.AddDays(-30))};h@example.org;Beta;1000;650.00");

    public const int HistoryPeriods = 4;
    public const int HistoryCupsTotal = 162;
    public const decimal HistoryCostTotal = 4132.00m;

    /// <summary>The final Excel round: A −538.00, B +120.00, and −10·NN for every placeholder.</summary>
    public static string OpeningPaste => string.Join('\n',
        new[] { "a@example.org;-538.00", "b@example.org;120,00" }
            .Concat(Enumerable.Range(1, PlaceholderCount)
                .Select(nn => $"{PlaceholderName(nn)};{OpeningAmountOf(nn).ToString("0.00", CultureInfo.InvariantCulture)}")));

    public const int OpeningRows = 29;
    public const decimal OpeningTotal = -4198.00m;

    /// <summary>Σ over the placeholders of 10·NN.</summary>
    public static decimal PlaceholderOpeningTotal => Enumerable.Range(1, PlaceholderCount).Sum(OpeningAmountOf);

    // ---- Expected nets per settlement (who → Net); placeholders by number. ----

    public static IReadOnlyDictionary<string, decimal> S1Nets => Nets(
        ("A", -616.00m), ("B", 165.00m), ("H", 4231.00m), Placeholders(nn => OpeningAmountOf(nn)));

    public static IReadOnlyDictionary<string, decimal> S2Nets => Nets(
        ("A", -14.00m), ("B", 14.00m), ("P05", 50.00m), ("P27", -270.00m), ("H", 220.00m));

    public static IReadOnlyDictionary<string, decimal> S4Nets => Nets(
        ("A", -595.50m), ("P27", 0.00m), ("B", 630.00m), ("P05", 50.00m), ("H", -84.50m));

    public static IReadOnlyDictionary<string, decimal> S5Nets => Nets(
        ("A", -595.50m), ("B", 0.00m), ("P05", 50.00m), ("H", 545.50m));

    /// <summary>Bag Y after 16c2: 1000 − 40 − 60 − 20 − 10 g, written off half to A and half to H at 0.70 per g.</summary>
    public const decimal YRemainder = 1000m - 40m - 60m - 20m - 10m;

    public const decimal YPricePerGram = 0.70m;
    public const decimal XPricePerGram = 0.50m;

    private static string Day(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static (string, decimal)[] Placeholders(Func<int, decimal> net) =>
        Enumerable.Range(1, PlaceholderCount).Select(nn => ($"P{nn:00}", net(nn))).ToArray();

    private static IReadOnlyDictionary<string, decimal> Nets(params object[] parts)
    {
        var nets = new Dictionary<string, decimal>();
        foreach (var part in parts)
        {
            switch (part)
            {
                case (string who, decimal net):
                    nets.Add(who, net);
                    break;
                case (string, decimal)[] many:
                    foreach (var (who, net) in many)
                        nets.Add(who, net);
                    break;
            }
        }

        return nets;
    }
}
