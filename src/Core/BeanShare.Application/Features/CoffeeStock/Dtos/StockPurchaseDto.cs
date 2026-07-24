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

    public decimal TotalCost => CostAmount;
    public string CoffeeType => ProductType;
    public string PurchasedByName { get; init; } = "Unknown"; // Will be populated from query
}