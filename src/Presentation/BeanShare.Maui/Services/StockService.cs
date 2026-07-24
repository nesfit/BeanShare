using System.Net.Http.Json;
using BeanShare.Application.Features.CoffeeStock.Dtos;

namespace BeanShare.Maui.Services;

public class StockService : IStockService
{
    private readonly HttpClient _httpClient;
    private readonly ApiResponseCache _cache;

    public StockService(HttpClient httpClient, ApiResponseCache cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    public async Task<CoffeeStockDto?> GetSpaceStockAsync(Guid spaceId, int page = 1, int pageSize = 10)
    {
        var cacheKey = $"stock:{spaceId}:p{page}";
        var cached = _cache.Get<CoffeeStockDto>(cacheKey);
        if (cached != null)
            return cached;

        var stale = _cache.GetStale<CoffeeStockDto>(cacheKey);

        try
        {
            var result = await _httpClient.GetFromJsonAsync<CoffeeStockDto>($"/api/spaces/{spaceId}/stock?page={page}&pageSize={pageSize}");
            if (result != null)
                _cache.Set(cacheKey, result);
            return result;
        }
        catch
        {
            return stale;
        }
    }

    public async Task<bool> AddStockPurchaseAsync(Guid spaceId, string productName, string coffeeType, int quantityGrams, decimal totalCost, string currency, DateTime purchasedAt, int bagCount = 1)
    {
        try
        {
            var request = new
            {
                ProductName = productName,
                ProductBrand = "Generic",
                ProductType = coffeeType,
                QuantityGrams = quantityGrams,
                CostAmount = totalCost,
                CostCurrency = currency,
                Vendor = "Store",
                PurchasedAt = purchasedAt.ToUniversalTime(),
                BagCount = bagCount
            };

            var response = await _httpClient.PostAsJsonAsync($"/api/spaces/{spaceId}/stock/purchases", request);
            if (response.IsSuccessStatusCode)
            {
                _cache.Invalidate("stock:");
            }
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateStockPurchaseAsync(Guid spaceId, Guid purchaseId, string productName, string productBrand, string coffeeType, decimal quantityGrams, decimal costAmount, DateTime purchasedAt)
    {
        try
        {
            var request = new
            {
                SpaceId = spaceId,
                PurchaseId = purchaseId,
                ProductName = productName,
                ProductBrand = productBrand,
                CoffeeType = coffeeType,
                QuantityGrams = quantityGrams,
                CostAmount = costAmount,
                PurchasedAt = purchasedAt.ToUniversalTime()
            };

            var response = await _httpClient.PutAsJsonAsync($"/api/spaces/{spaceId}/stock/purchases/{purchaseId}", request);
            if (response.IsSuccessStatusCode)
            {
                _cache.Invalidate("stock:");
            }
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteStockPurchaseAsync(Guid spaceId, Guid purchaseId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/spaces/{spaceId}/stock/purchases/{purchaseId}");
            if (response.IsSuccessStatusCode)
            {
                _cache.Invalidate("stock:");
            }
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SetCurrentlyUsedAsync(Guid spaceId, Guid? stockLevelId)
    {
        try
        {
            bool success;
            if (stockLevelId.HasValue)
            {
                var response = await _httpClient.PostAsync($"/api/spaces/{spaceId}/stock/{stockLevelId.Value}/currently-used", null);
                success = response.IsSuccessStatusCode;
            }
            else
            {
                var response = await _httpClient.DeleteAsync($"/api/spaces/{spaceId}/stock/currently-used");
                success = response.IsSuccessStatusCode;
            }

            if (success)
                _cache.Invalidate("stock:");

            return success;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> OpenBagAsync(Guid spaceId, Guid lotId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/spaces/{spaceId}/stock/bags/{lotId}/open", null);
            if (response.IsSuccessStatusCode)
                _cache.Invalidate("stock:");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> MarkBagEmptyAsync(Guid spaceId, Guid lotId, string? reason = null)
    {
        try
        {
            var request = new { Reason = reason ?? "Bag emptied" };
            var response = await _httpClient.PostAsJsonAsync($"/api/spaces/{spaceId}/stock/bags/{lotId}/empty", request);
            if (response.IsSuccessStatusCode)
                _cache.Invalidate("stock:");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
