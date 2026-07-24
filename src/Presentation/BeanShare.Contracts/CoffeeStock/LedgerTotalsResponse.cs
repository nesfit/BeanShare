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
}
