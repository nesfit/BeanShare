namespace BeanShare.Contracts.CoffeeStock;

public sealed record AddStockPurchaseRequest
{
    public Guid? BodySpaceId { get; init; }
    public required string ProductName { get; init; }
    public required string ProductBrand { get; init; }
    public required string ProductType { get; init; }
    public required decimal QuantityGrams { get; init; }
    public required decimal CostAmount { get; init; }
    public required string CostCurrency { get; init; }
    public required string Vendor { get; init; }
    public required DateTime PurchasedAt { get; init; }

    // Number of identical bags of this size/price to record as separate lots. Defaults to 1.
    public int BagCount { get; init; } = 1;
}