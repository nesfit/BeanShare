using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Common;
using CoffeeStockAggregate = BeanShare.Domain.Aggregates.CoffeeStock.CoffeeStock;

namespace BeanShare.Application.Features.Settlement;

/// <summary>
/// The pre-ledger "Paid" of a legacy settlement: purchases in the period's window, or every purchase up
/// to its end when the window has none. Frozen once by the backfill; computed live only until then.
/// </summary>
public static class LegacyPaid
{
    public static IReadOnlyDictionary<UserId, decimal> Compute(CoffeeStockAggregate? stock, BillingPeriod period, string currency)
    {
        var purchases = stock?.Purchases ?? [];
        var window = purchases
            .Where(p => p.PurchasedAt >= period.StartDate && p.PurchasedAt <= period.EndDate)
            .ToList();

        if (window.Count == 0)
            window = purchases.Where(p => p.PurchasedAt <= period.EndDate).ToList();

        return window
            .Where(p => p.Cost.Currency.Code == currency)
            .GroupBy(p => p.PurchasedBy)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Cost.Amount));
    }
}
