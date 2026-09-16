namespace BeanShare.Contracts.CoffeeStock;

public sealed record StockPurchaseResponse
{
    public required Guid Id { get; init; }
    public required string ProductName { get; init; }
    public required string ProductBrand { get; init; }
    public required string ProductType { get; init; }
    public required decimal QuantityGrams { get; init; }
    public required decimal CostAmount { get; init; }
    public required string CostCurrency { get; init; }
    public required string Vendor { get; init; }
    public required Guid PurchasedBy { get; init; }
    public required DateTime PurchasedAt { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required decimal CostPerGram { get; init; }
    public required string Message { get; init; }

    // Per-bag (lot) state — mirrors StockPurchaseDto. Non-required with defaults so existing
    // response initializers keep compiling; populated where the source bag is known.
    public decimal ConsumedGrams { get; init; }
    public decimal RemainingGrams { get; init; }
    public string Status { get; init; } = "Sealed";
    public string Label { get; init; } = "";
    public bool IsOpen { get; init; }
    public DateTime? OpenedAt { get; init; }
    public DateTime? DepletedAt { get; init; }
    public string PurchasedByName { get; init; } = "Unknown";

    /// <summary>Grams billed to drinkers as a write-off when an admin emptied this bag (0 = ran out cup by cup).</summary>
    public decimal WrittenOffGrams { get; init; }
    /// <summary>Grams an admin discarded unbilled via POST .../bags/{lotId}/discard (stale/spilled coffee).</summary>
    public decimal DiscardedGrams { get; init; }
    /// <summary>True while the admin's "mark empty"/"discard" can still be undone via POST .../bags/{lotId}/undo-empty.</summary>
    public bool CanUndoEmpty { get; init; }
    /// <summary>When the undo window closes; null when there is nothing to undo.</summary>
    public DateTime? UndoEmptyUntil { get; init; }
}