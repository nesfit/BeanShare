using BeanShare.Domain.Aggregates.Settlement;
using BeanShare.Domain.Common;

namespace BeanShare.Domain.Services;

/// <summary>A cup as money sees it: <see cref="StampedCost"/> is null when it is priced by the fallback rate.</summary>
/// <param name="Payer">Effective payer of the stamped bag; null credits the fallback payers pro rata.</param>
public sealed record LedgerCup(UserId Drinker, decimal Grams, decimal? StampedCost, UserId? Payer);

/// <summary>A space-currency purchase that prices (and is credited for) fallback cups.</summary>
public sealed record LedgerPurchase(UserId Payer, decimal Cost, decimal Grams);

public sealed record LedgerEntry(UserId UserId, UserId CounterpartyUserId, decimal Amount);

public sealed record LedgerCalculationInput(
    IReadOnlyList<LedgerCup> Cups,
    IReadOnlyList<LedgerPurchase> FallbackPurchases,
    IReadOnlyList<LedgerEntry> Entries,
    IReadOnlyDictionary<UserId, decimal> CarriedIn,
    UserId? Treasurer,
    int Decimals);

/// <param name="UnassignedRounding">
/// The rounding residual when no treasurer is set to carry it, so ΣNet + UnassignedRounding = 0; otherwise 0.
/// </param>
public sealed record LedgerCalculation(IReadOnlyList<LedgerLineSnapshot> Lines, decimal FallbackRate, decimal UnassignedRounding = 0m)
{
    public LedgerLineSnapshot? LineFor(UserId userId) => Lines.FirstOrDefault(l => l.UserId == userId);

    /// <summary>The member's position: the line's Net, or 0 without a line.</summary>
    public decimal NetFor(UserId userId) => LineFor(userId)?.Net ?? 0m;
}

public sealed record LedgerTransfer(UserId From, UserId To, decimal Amount);

/// <summary>
/// The single money calculation behind generation, the settlement preview and the running balance
/// (design §4.1, DS-19). Pure: debit and credit come from the same cups, entries are two-party, the
/// carry sums to zero and the rounding residual lands on the treasurer, so ΣNet = 0 exactly when a
/// treasurer is set; without one the residual is reported as UnassignedRounding (ΣNet + it = 0).
/// </summary>
public static class SettlementLedgerCalculator
{
    public static LedgerCalculation Calculate(LedgerCalculationInput input)
    {
        var decimals = input.Decimals;
        var fallbackGrams = input.FallbackPurchases.Sum(p => p.Grams);
        var fallbackCost = input.FallbackPurchases.Sum(p => p.Cost);
        var rate = fallbackGrams > 0 ? fallbackCost / fallbackGrams : 0m;
        var fallbackPayers = input.FallbackPurchases
            .GroupBy(p => p.Payer)
            .Select(g => (Payer: g.Key, Cost: g.Sum(p => p.Cost)))
            .ToList();

        var grams = new Dictionary<UserId, decimal>();
        var costRaw = new Dictionary<UserId, decimal>();
        var creditRaw = new Dictionary<UserId, decimal>();

        foreach (var cup in input.Cups)
        {
            Add(grams, cup.Drinker, cup.Grams);

            var debit = cup.StampedCost ?? cup.Grams * rate;
            Add(costRaw, cup.Drinker, debit);

            if (cup.StampedCost.HasValue && cup.Payer is { } payer)
            {
                Add(creditRaw, payer, debit);
            }
            else if (fallbackCost > 0)
            {
                foreach (var (fallbackPayer, paid) in fallbackPayers)
                    Add(creditRaw, fallbackPayer, debit * paid / fallbackCost);
            }
        }

        var cost = costRaw.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, decimals));
        var credit = creditRaw.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, decimals));

        var adjustments = new Dictionary<UserId, decimal>();
        foreach (var entry in input.Entries)
        {
            Add(adjustments, entry.UserId, entry.Amount);
            Add(adjustments, entry.CounterpartyUserId, -entry.Amount);
        }

        var residual = cost.Values.Sum() - credit.Values.Sum();
        var rounding = new Dictionary<UserId, decimal>();
        if (input.Treasurer is { } treasurer)
            rounding[treasurer] = residual;

        var users = grams.Keys
            .Concat(cost.Keys).Concat(credit.Keys).Concat(adjustments.Keys)
            .Concat(input.CarriedIn.Keys).Concat(rounding.Keys)
            .Distinct()
            .OrderBy(u => u.Value)
            .ToList();

        var lines = users
            .Select(u => new LedgerLineSnapshot(
                u,
                grams.GetValueOrDefault(u),
                null,
                cost.GetValueOrDefault(u),
                credit.GetValueOrDefault(u),
                input.CarriedIn.GetValueOrDefault(u),
                adjustments.GetValueOrDefault(u),
                rounding.GetValueOrDefault(u)))
            .Where(l => l.CoffeeGrams != 0 || l.Cost != 0 || l.Credit != 0 || l.CarriedIn != 0 || l.Adjustments != 0 || l.Rounding != 0)
            .ToList();

        if (lines.Count > 0 && input.Treasurer is { } t && lines.All(l => l.UserId != t))
            lines.Add(new LedgerLineSnapshot(t, 0m, null, 0m, 0m, 0m, 0m, 0m));

        return new LedgerCalculation(lines, rate, input.Treasurer is null ? residual : 0m);
    }

    /// <summary>
    /// The star plan (DS-3): every debtor pays the treasurer, the treasurer pays every creditor, in
    /// exact cents from the lines' Net.
    /// </summary>
    public static IReadOnlyList<LedgerTransfer> StarTransfers(IEnumerable<(UserId UserId, decimal Net)> lines, UserId treasurer) =>
        lines
            .Where(l => l.UserId != treasurer && l.Net != 0)
            .Select(l => l.Net < 0
                ? new LedgerTransfer(l.UserId, treasurer, -l.Net)
                : new LedgerTransfer(treasurer, l.UserId, l.Net))
            .ToList();

    private static void Add(Dictionary<UserId, decimal> totals, UserId userId, decimal amount) =>
        totals[userId] = totals.GetValueOrDefault(userId) + amount;
}
