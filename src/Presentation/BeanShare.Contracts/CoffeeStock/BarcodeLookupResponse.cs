namespace BeanShare.Contracts.CoffeeStock;

/// <summary>Result of resolving a scanned barcode into prefill data for adding a bag.</summary>
public sealed record BarcodeLookupResponse(
    bool Found,
    string? ProductName,
    string? ProductBrand,
    string? ProductType,
    int? QuantityGrams,
    decimal? CostAmount,
    string Source);
