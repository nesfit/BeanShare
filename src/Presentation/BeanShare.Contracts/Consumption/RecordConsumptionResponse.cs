namespace BeanShare.Contracts.Consumption;

public sealed record RecordConsumptionResponse
{
    public required Guid SpaceId { get; init; }
    public required string ProductName { get; init; }
    public required string ProductBrand { get; init; }
    public required string ProductType { get; init; }
    public required decimal ConsumedGrams { get; init; }
    public required decimal RemainingGrams { get; init; }
    public required DateTime ConsumedAt { get; init; }

    // Cost stamped from the drawn bag's price at log time (null for legacy/unattributed cups).
    public decimal? CostAmount { get; init; }
    public string? CostCurrency { get; init; }
}