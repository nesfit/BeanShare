namespace BeanShare.Application.Features.Consumption.Dtos;
public sealed class ConsumptionEntryDto
{
    public required Guid Id { get; init; }
    public required Guid SpaceId { get; init; }
    public required Guid UserId { get; init; }
    public string UserName { get; init; } = "Unknown";
    public required string ProductName { get; init; }
    public required string ProductBrand { get; init; }
    public required string ProductType { get; init; }
    public required decimal QuantityGrams { get; init; }
    public required decimal RemainingGrams { get; init; }
    public required DateTime ConsumedAt { get; init; }
    public required DateTime CreatedAt { get; init; }
    public bool CanEdit { get; init; }

    public Guid? PresetId { get; init; }
    public string? PresetName { get; init; }
    public int CoffeeGrams => (int)QuantityGrams;

    // Per-bag cost stamped at log time (null for legacy/unattributed cups).
    public decimal? CostAmount { get; init; }
    public string? CostCurrency { get; init; }

    // CanEdit is false for a write-off share, which can only be deleted.
    public bool CanDelete { get; init; }
}