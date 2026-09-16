namespace BeanShare.Contracts.CoffeeStock;

/// <summary>Result of undoing an admin's "mark empty": the write-off cups removed and the bag restored.</summary>
public sealed record UndoBagEmptyResponse
{
    public required Guid LotId { get; init; }
    public required string ProductDisplayName { get; init; }
    public required string BagLabel { get; init; }
    /// <summary>Grams put back into the bag.</summary>
    public required decimal RestoredGrams { get; init; }
    /// <summary>How many billed (phantom-fill) cups were deleted.</summary>
    public required int RemovedEntries { get; init; }
    /// <summary>The bag's status after the undo: "Sealed" if nothing had been poured from it, else "Open".</summary>
    public required string NewStatus { get; init; }
    public required string Message { get; init; }
}
