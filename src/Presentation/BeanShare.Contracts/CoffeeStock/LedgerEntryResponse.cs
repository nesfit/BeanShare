namespace BeanShare.Contracts.CoffeeStock;

/// <summary>One stock movement: a purchase (+) or a cup (−), tied to its bag. Mirrors LedgerEntryDto.</summary>
public sealed record LedgerEntryResponse
{
    public required DateTime Timestamp { get; init; }
    public required string Kind { get; init; }                // "Purchase" | "Cup"
    public required string ProductDisplayName { get; init; }
    public required string ProductType { get; init; }
    public required string BagLabel { get; init; }
    public required decimal DeltaGrams { get; init; }         // + for a purchase, − for a cup
    public required decimal RunningBalanceGrams { get; init; }
    public required Guid UserId { get; init; }
    public required string UserName { get; init; }
    public decimal? CostAmount { get; init; }
    public string? CostCurrency { get; init; }
    public string? Note { get; init; }
}
