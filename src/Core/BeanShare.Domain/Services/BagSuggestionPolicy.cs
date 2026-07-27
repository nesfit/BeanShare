using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;

namespace BeanShare.Domain.Services;

/// <summary>One bag scored against the recent-cup window, ready to be offered in a log picker.</summary>
public sealed record RankedBag(
    Purchase Lot,
    decimal Score,
    int RecentCups,
    int MyRecentCups,
    DateTime? LastUsedAt,
    bool IsSuggested);

/// <summary>
/// Picks which bag a manually logged cup should most likely be drawn from.
/// <para>
/// Open bags always outrank sealed ones — you brew from what is open — and among them the winner is
/// whichever bag the space has actually been pouring from lately: a histogram over the last
/// <see cref="DefaultWindowSize"/> cups of the space, with the caller's own cups weighted heavier
/// (<see cref="OwnCupWeight"/>) so a personal bag beats a colleague's when both are open.
/// </para>
/// <para>
/// Everything is ranked, never filtered — a sealed bag simply sorts last, so the picker can still
/// offer it and <see cref="Purchase.Consume"/> will auto-open it.
/// </para>
/// </summary>
public static class BagSuggestionPolicy
{
    /// <summary>How many of the space's most recent cups form the histogram window.</summary>
    public const int DefaultWindowSize = 20;

    public const decimal OwnCupWeight = 2m;
    public const decimal OtherCupWeight = 1m;

    /// <summary>
    /// Rank <paramref name="lots"/> best-first for <paramref name="currentUser"/>. Empty bags are
    /// dropped; the head of the result (if any) is flagged <see cref="RankedBag.IsSuggested"/>.
    /// </summary>
    /// <param name="spaceEntries">
    /// The space's consumption entries, any order. Only the newest <paramref name="windowSize"/>
    /// count — including ones with no bag attribution, which take up a slot without voting, so the
    /// window keeps meaning "of the last N cups".
    /// </param>
    public static IReadOnlyList<RankedBag> Rank(
        IEnumerable<Purchase> lots,
        IEnumerable<ConsumptionEntry> spaceEntries,
        UserId currentUser,
        int windowSize = DefaultWindowSize)
    {
        ArgumentNullException.ThrowIfNull(lots);
        ArgumentNullException.ThrowIfNull(spaceEntries);

        var candidates = lots.Where(l => !l.IsEmpty).ToList();
        if (candidates.Count == 0)
            return [];

        var window = spaceEntries
            .OrderByDescending(e => e.ConsumedAt)
            .ThenByDescending(e => e.CreatedAt)
            .Take(Math.Max(0, windowSize))
            .ToList();

        var tally = new Dictionary<Guid, Tally>();
        foreach (var entry in window)
        {
            if (!entry.StockLotId.HasValue)
                continue;

            var lotId = entry.StockLotId.Value;
            var mine = entry.UserId == currentUser;

            tally.TryGetValue(lotId, out var current);
            tally[lotId] = new Tally(
                Score: current.Score + (mine ? OwnCupWeight : OtherCupWeight),
                Cups: current.Cups + 1,
                MyCups: current.MyCups + (mine ? 1 : 0),
                LastUsedAt: current.LastUsedAt is { } seen && seen > entry.ConsumedAt ? seen : entry.ConsumedAt);
        }

        var ranked = candidates
            .Select(lot => (Lot: lot, Stats: tally.TryGetValue(lot.Id, out var t) ? t : default))
            .OrderByDescending(x => x.Lot.IsOpen)
            .ThenByDescending(x => x.Stats.Score)
            .ThenByDescending(x => x.Stats.LastUsedAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.Lot.OpenedAt ?? DateTime.MinValue)
            // Same score and never poured from: fall in line with ConsumeStock's own fallback and
            // offer the oldest bag first, so stock rotates instead of ageing out.
            .ThenBy(x => x.Lot.PurchasedAt)
            .ThenBy(x => x.Lot.CreatedAt)
            .ToList();

        return ranked
            .Select((x, index) => new RankedBag(
                x.Lot,
                x.Stats.Score,
                x.Stats.Cups,
                x.Stats.MyCups,
                x.Stats.LastUsedAt,
                IsSuggested: index == 0))
            .ToList();
    }

    private readonly record struct Tally(decimal Score, int Cups, int MyCups, DateTime? LastUsedAt);
}
