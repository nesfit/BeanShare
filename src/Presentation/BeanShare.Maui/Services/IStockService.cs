using BeanShare.Application.Features.CoffeeStock.Dtos;
using BeanShare.Contracts.CoffeeStock;

namespace BeanShare.Maui.Services;

public interface IStockService
{
    Task<CoffeeStockDto?> GetSpaceStockAsync(Guid spaceId, int page = 1, int pageSize = 10);
    Task<bool> AddStockPurchaseAsync(Guid spaceId, string productName, string coffeeType, int quantityGrams, decimal totalCost, string currency, DateTime purchasedAt, int bagCount = 1, string? barcode = null);

    /// <summary>Resolves a scanned barcode into prefill data (from this space's history, else a product catalog).</summary>
    Task<BarcodeLookupResponse?> LookupBarcodeAsync(Guid spaceId, string barcode);
    Task<bool> UpdateStockPurchaseAsync(Guid spaceId, Guid purchaseId, string productName, string productBrand, string coffeeType, decimal quantityGrams, decimal costAmount, DateTime purchasedAt);
    Task<bool> DeleteStockPurchaseAsync(Guid spaceId, Guid purchaseId);
    Task<bool> SetCurrentlyUsedAsync(Guid spaceId, Guid? stockLevelId);

    /// <summary>Marks a specific bag (lot) as the one currently being poured from.</summary>
    Task<bool> OpenBagAsync(Guid spaceId, Guid lotId);

    /// <summary>Empties a bag; the untracked remainder is billed to its drinkers (phantom-fill).</summary>
    Task<bool> MarkBagEmptyAsync(Guid spaceId, Guid lotId, string? reason = null);

    /// <summary>Undoes an admin's "mark empty"/"discard" within its 24 h window: removes any billed cups and restores the bag.</summary>
    Task<bool> UndoBagEmptyAsync(Guid spaceId, Guid lotId);

    /// <summary>Throws a bag's remainder away without billing anyone (stale/spilled coffee).</summary>
    Task<bool> DiscardBagAsync(Guid spaceId, Guid lotId);
}
