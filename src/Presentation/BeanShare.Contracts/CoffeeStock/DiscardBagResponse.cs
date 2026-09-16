namespace BeanShare.Contracts.CoffeeStock;

/// <summary>Result of discarding a bag's remainder unbilled: what was thrown away and what it had cost.</summary>
public sealed record DiscardBagResponse
{
    public required Guid LotId { get; init; }
    public required string ProductDisplayName { get; init; }
    public required string BagLabel { get; init; }
    /// <summary>Grams thrown away; nobody is billed for them.</summary>
    public required decimal DiscardedGrams { get; init; }
    /// <summary>What those grams had cost the space, at the bag's own price per gram.</summary>
    public required decimal SunkCostAmount { get; init; }
    public required string Currency { get; init; }
    public required string Message { get; init; }
}
