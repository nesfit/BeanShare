using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Queries.LookupBarcode;

public sealed class LookupBarcodeQueryHandler : IRequestHandler<LookupBarcodeQuery, Result<BarcodeLookupDto>>
{
    private static readonly BarcodeLookupDto NotFound =
        new(false, null, null, null, null, null, "none");

    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IProductCatalogService _catalog;

    public LookupBarcodeQueryHandler(
        ICoffeeStockRepository coffeeStockRepository,
        IProductCatalogService catalog)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _catalog = catalog;
    }

    public async Task<Result<BarcodeLookupDto>> Handle(LookupBarcodeQuery query, CancellationToken cancellationToken)
    {
        var barcode = query.Barcode?.Trim();
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return Result<BarcodeLookupDto>.Success(NotFound);
        }

        // 1. This space's own history — remembers the last product AND price for the barcode.
        var spaceId = new SpaceId(query.SpaceId);
        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        var lastForBarcode = coffeeStock?.Purchases
            .Where(p => string.Equals(p.Barcode, barcode, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.PurchasedAt)
            .FirstOrDefault();

        if (lastForBarcode is not null)
        {
            return Result<BarcodeLookupDto>.Success(new BarcodeLookupDto(
                true,
                lastForBarcode.Product.Name,
                lastForBarcode.Product.Brand,
                lastForBarcode.Product.Type.ToString(),
                (int)Math.Round(lastForBarcode.Quantity.Grams),
                lastForBarcode.Cost.Amount,
                "history"));
        }

        // 2. External catalog (best-effort, no price).
        try
        {
            var catalog = await _catalog.LookupAsync(barcode, cancellationToken);
            if (catalog is not null && (!string.IsNullOrWhiteSpace(catalog.Name) || !string.IsNullOrWhiteSpace(catalog.Brand)))
            {
                return Result<BarcodeLookupDto>.Success(new BarcodeLookupDto(
                    true,
                    catalog.Name,
                    catalog.Brand,
                    null,
                    catalog.QuantityGrams,
                    null,
                    "catalog"));
            }
        }
        catch
        {
            // Catalog is best-effort; fall through to "not found".
        }

        return Result<BarcodeLookupDto>.Success(NotFound);
    }
}
