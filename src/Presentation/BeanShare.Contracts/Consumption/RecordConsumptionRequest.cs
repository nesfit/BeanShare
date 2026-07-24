namespace BeanShare.Contracts.Consumption;

public sealed record RecordConsumptionRequest
{
    public required Guid SpaceId { get; init; }
    public required string ProductName { get; init; }
    public required string ProductBrand { get; init; }
    public required string ProductType { get; init; }
    public required decimal QuantityGrams { get; init; }
    public DateTime? ConsumedAt { get; init; }
    public string? PresetName { get; init; }
    public Guid? ForUserId { get; init; }

    // Optional bag (Purchase lot) to draw this cup from. Null = the product's open bag (then oldest).
    public Guid? StockLotId { get; init; }
}