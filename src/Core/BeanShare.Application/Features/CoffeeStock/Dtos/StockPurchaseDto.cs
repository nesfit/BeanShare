namespace BeanShare.Application.Features.CoffeeStock.Dtos;
public sealed class StockPurchaseDto
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

    // --- Per-bag (lot) state ---
    public decimal ConsumedGrams { get; init; }
    public decimal RemainingGrams { get; init; }
    public string Status { get; init; } = "Sealed";
    public string Label { get; init; } = "";
    public bool IsOpen { get; init; }
    public DateTime? OpenedAt { get; init; }
    public DateTime? DepletedAt { get; init; }

    /// <summary>Grams billed to drinkers as a write-off when an admin emptied this bag (0 = ran out cup by cup).</summary>
    public decimal WrittenOffGrams { get; init; }
    /// <summary>Grams an admin discarded unbilled (stale/spilled coffee; the space eats the cost).</summary>
    public decimal DiscardedGrams { get; init; }
    /// <summary>True while the admin's "mark empty"/"discard" can still be undone (within its 24 h window).</summary>
    public bool CanUndoEmpty { get; init; }
    /// <summary>When the undo window closes; null when there is nothing to undo.</summary>
    public DateTime? UndoEmptyUntil { get; init; }

    public decimal TotalCost => CostAmount;
    public string CoffeeType => ProductType;
    public string PurchasedByName { get; init; } = "Unknown"; // Will be populated from query

    /// <summary>Who paid for the bag when not the recorder; null means <see cref="PurchasedBy"/> paid.</summary>
    public Guid? PaidByUserId { get; init; }
    public string? PaidByName { get; init; }

    /// <summary>The member credited when cups are drawn from this bag.</summary>
    public Guid EffectivePayerId => PaidByUserId ?? PurchasedBy;

    /// <summary>"Paid in Excel" (DS-41): the bag earns no BeanShare credit and is left out of the fallback price.</summary>
    public bool ExcludedFromLedgerCredit { get; init; }

    /// <summary>Grams of phantom write-off rows an admin voided (DS-42); gone for good, billed to nobody.</summary>
    public decimal VoidedGrams { get; init; }
}