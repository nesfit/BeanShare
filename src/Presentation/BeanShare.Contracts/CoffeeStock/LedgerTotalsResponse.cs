namespace BeanShare.Contracts.CoffeeStock;

/// <summary>Aggregate totals across the (filtered) ledger. Mirrors LedgerTotalsDto.</summary>
public sealed record LedgerTotalsResponse
{
    public required decimal PurchasedGrams { get; init; }
    public required decimal PurchasedCost { get; init; }
    public required decimal ConsumedGrams { get; init; }
    public required decimal ConsumedCost { get; init; }
    public required decimal NetGrams { get; init; }
    public required int PurchaseCount { get; init; }
    public required int CupCount { get; init; }
    public required string Currency { get; init; }
    /// <summary>Grams an admin threw away unbilled (stale/spilled bags).</summary>
    public decimal DiscardedGrams { get; init; }
    /// <summary>Sunk value of the discarded grams at the bags' own cost per gram.</summary>
    public decimal DiscardedCost { get; init; }
    public int DiscardCount { get; init; }
}
