using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.CoffeeStock.Queries.LookupBarcode;

/// <summary>
/// Resolve a scanned barcode into prefill data for adding a bag: first from this space's own
/// purchase history (so the last price paid is remembered), then from an external product catalog
/// (name/brand/weight only, no price). Always succeeds; <see cref="BarcodeLookupDto.Found"/> is
/// false when nothing matched.
/// </summary>
[RequireSpaceMember("SpaceId")]
public sealed record LookupBarcodeQuery(
    Guid SpaceId,
    string Barcode
) : IAuthorize, IQuery<Result<BarcodeLookupDto>>;

public sealed record BarcodeLookupDto(
    bool Found,
    string? ProductName,
    string? ProductBrand,
    string? ProductType,
    int? QuantityGrams,
    decimal? CostAmount,
    string Source);   // "history" | "catalog" | "none"
