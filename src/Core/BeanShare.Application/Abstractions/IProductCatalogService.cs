namespace BeanShare.Application.Abstractions;

/// <summary>
/// Looks up product metadata (name, brand, net weight) by barcode from an external catalog
/// (e.g. Open Food Facts). Best-effort — implementations return <c>null</c> when the product
/// is unknown or the service is unavailable, and must never throw for a missing product.
/// </summary>
public interface IProductCatalogService
{
    Task<ProductCatalogResult?> LookupAsync(string barcode, CancellationToken cancellationToken = default);
}

/// <summary>A product resolved from an external catalog. Fields are null when unknown.</summary>
public sealed record ProductCatalogResult(string? Name, string? Brand, int? QuantityGrams);
