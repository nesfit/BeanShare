using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BeanShare.Infrastructure.Persistence.Services;

/// <summary>
/// One-time reconstruction of per-bag (lot) state from the pre-per-bag data, run once after the
/// schema migration and before seeding. It reconstructs bag <em>states</em> only — labels each
/// purchase "#N" per product and fills bags FIFO with that product's total consumed grams (capped at
/// each bag's capacity) so remaining stock and Open/Empty/Sealed status match reality. It deliberately
/// does NOT retro-attribute or retro-bill individual historical cups: those keep null StockLotId/Cost
/// (settlement falls back to weighted-average for them). Only cups logged after the migration are
/// stamped per bag. Idempotent: a no-op once any purchase carries a label.
/// </summary>
public sealed class LotBackfillService
{
    private readonly BeanShareDbContext _context;
    private readonly IClock _clock;
    private readonly ILogger<LotBackfillService> _logger;

    public LotBackfillService(BeanShareDbContext context, IClock clock, ILogger<LotBackfillService> logger)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Idempotency marker: once any purchase has a non-empty label the back-fill has already run.
        var alreadyRun = await _context.Set<Purchase>().AnyAsync(p => p.Label != "", cancellationToken);
        if (alreadyRun)
            return;

        var stocks = await _context.CoffeeStocks
            .Include(cs => cs.Purchases)
            .ToListAsync(cancellationToken);

        if (stocks.Count == 0)
            return;

        // Sum consumed grams per (space, product). Materialize then group in memory — Weight is a
        // value-converted property, so `.Grams` can't be summed in SQL, and volumes are modest.
        var consumptions = await _context.Consumptions.AsNoTracking().ToListAsync(cancellationToken);
        var consumedBySpaceProduct = consumptions
            .GroupBy(c => (c.SpaceId, Key: ProductKey(c.Product)))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Quantity.Grams));

        var totalOverflow = 0m;

        foreach (var stock in stocks)
        {
            var byProduct = stock.Purchases.GroupBy(p => ProductKey(p.Product));
            foreach (var group in byProduct)
            {
                var bags = group
                    .OrderBy(p => p.PurchasedAt)
                    .ThenBy(p => p.CreatedAt)
                    .ToList();

                var totalConsumed = consumedBySpaceProduct.GetValueOrDefault((stock.SpaceId, group.Key), 0m);
                var overflow = ReconstructProductBags(bags, totalConsumed, _clock);

                if (overflow > 0)
                {
                    totalOverflow += overflow;
                    _logger.LogWarning(
                        "Lot back-fill: product '{Product}' in space {Space} was over-consumed by {Overflow}g beyond its {Bags} bag(s); the excess is left unattributed.",
                        group.Key, stock.SpaceId.Value, overflow, bags.Count);
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Lot back-fill complete: reconstructed bag state for {Stocks} coffee stock(s); {Overflow}g total over-consumption left unattributed.",
            stocks.Count, totalOverflow);
    }

    /// <summary>
    /// Label the FIFO-ordered bags "#1..#n" and fill them with <paramref name="totalConsumedGrams"/>
    /// (capped at each bag's capacity) using the domain's own <see cref="Purchase.Consume"/> so the
    /// Sealed→Open→Empty transitions, OpenedAt/DepletedAt, and RemainingGrams are exactly consistent
    /// with normal consumption. Returns grams that could not be placed (over-consumption beyond total
    /// purchases). Pure over the passed bags — unit-testable without a database.
    /// </summary>
    public static decimal ReconstructProductBags(IReadOnlyList<Purchase> bagsFifoOrdered, decimal totalConsumedGrams, IClock clock)
    {
        var remaining = totalConsumedGrams;

        for (var i = 0; i < bagsFifoOrdered.Count; i++)
        {
            var bag = bagsFifoOrdered[i];
            bag.SetLabel($"#{i + 1}");

            if (remaining <= 0)
                continue;

            var fill = Math.Min(remaining, bag.Quantity.Grams);
            var weight = Weight.FromGrams(fill);
            if (weight.IsPositive)
            {
                bag.Consume(weight, clock);
                remaining -= weight.Grams;
            }
        }

        return remaining > 0 ? remaining : 0m;
    }

    private static (string, string, CoffeeType) ProductKey(CoffeeProduct p) =>
        (p.Name.ToLowerInvariant(), p.Brand.ToLowerInvariant(), p.Type);
}
