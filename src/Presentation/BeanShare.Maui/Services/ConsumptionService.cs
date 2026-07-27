using System.Net.Http.Json;
using System.Web;
using BeanShare.Application.Features.Consumption.Queries;
using BeanShare.Application.Features.Consumption.Queries.GetBagSuggestions;
using BeanShare.Application.Features.Consumption.Dtos;

namespace BeanShare.Maui.Services;

public class ConsumptionService : IConsumptionService
{
    private readonly HttpClient _httpClient;
    private readonly ApiResponseCache _cache;

    public ConsumptionService(HttpClient httpClient, ApiResponseCache cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    public async Task<GetRecentConsumptionsResult?> GetRecentConsumptionsAsync(Guid spaceId, Guid? forUserId = null)
    {
        var cacheKey = $"consumption:{spaceId}:{forUserId}";
        var fresh = _cache.Get<GetRecentConsumptionsResult>(cacheKey);
        if (fresh != null)
            return fresh;

        var stale = _cache.GetStale<GetRecentConsumptionsResult>(cacheKey);

        try
        {
            var url = $"/api/spaces/{spaceId}/consumption";
            if (forUserId.HasValue)
                url += $"?forUserId={forUserId.Value}";

            var result = await _httpClient.GetFromJsonAsync<GetRecentConsumptionsResult>(url);
            if (result != null)
                _cache.Set(cacheKey, result, TimeSpan.FromSeconds(15));
            return result;
        }
        catch
        {
            return stale;
        }
    }

    public async Task<BagSuggestionsResult?> GetBagSuggestionsAsync(Guid spaceId)
    {
        var cacheKey = $"consumption:bags:{spaceId}";
        var fresh = _cache.Get<BagSuggestionsResult>(cacheKey);
        if (fresh != null)
            return fresh;

        var stale = _cache.GetStale<BagSuggestionsResult>(cacheKey);

        try
        {
            var result = await _httpClient.GetFromJsonAsync<BagSuggestionsResult>(
                $"/api/spaces/{spaceId}/consumption/bag-suggestions");
            if (result != null)
                _cache.Set(cacheKey, result, TimeSpan.FromSeconds(15));
            return result;
        }
        catch
        {
            return stale;
        }
    }

    public async Task<bool> RecordConsumptionAsync(Guid spaceId, int grams, DateTime consumedAt)
    {
        return await RecordConsumptionAsync(spaceId, "Coffee", "Generic", "Ground", grams, consumedAt);
    }

    public async Task<bool> RecordConsumptionAsync(Guid spaceId, string productName, string productBrand, string productType, int grams, DateTime consumedAt, string? presetName = null, Guid? forUserId = null, Guid? stockLotId = null)
    {
        try
        {
            var request = new
            {
                SpaceId = spaceId,
                ProductName = productName,
                ProductBrand = productBrand,
                ProductType = productType,
                QuantityGrams = (decimal)grams,
                ConsumedAt = consumedAt,
                PresetName = presetName,
                ForUserId = forUserId,
                StockLotId = stockLotId
            };

            var response = await _httpClient.PostAsJsonAsync("/api/consumptions", request);
            if (response.IsSuccessStatusCode)
                _cache.Invalidate("consumption:");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ImportConsumptionCsvResult?> ImportCsvAsync(Guid spaceId, IReadOnlyList<ImportConsumptionCsvRow> rows)
    {
        try
        {
            var request = new
            {
                SpaceId = spaceId,
                Rows = rows.Select(r => new
                {
                    r.RowNumber,
                    r.Email,
                    r.ProductName,
                    r.ProductBrand,
                    r.ProductType,
                    r.QuantityGrams,
                    r.ConsumedAt
                }).ToList()
            };

            var response = await _httpClient.PostAsJsonAsync("/api/consumptions/import-csv", request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ImportConsumptionCsvResult>();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> UpdateConsumptionAsync(Guid id, Guid spaceId, string productName, string productBrand, string productType, decimal quantityGrams, DateTime consumedAt)
    {
        try
        {
            var request = new
            {
                Id = id,
                SpaceId = spaceId,
                ProductName = productName,
                ProductBrand = productBrand,
                ProductType = productType,
                QuantityGrams = quantityGrams,
                ConsumedAt = consumedAt
            };

            var response = await _httpClient.PutAsJsonAsync($"/api/consumptions/{id}", request);
            if (response.IsSuccessStatusCode)
                _cache.Invalidate("consumption:");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteConsumptionAsync(Guid id, Guid spaceId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/consumptions/{id}?spaceId={spaceId}");
            if (response.IsSuccessStatusCode)
                _cache.Invalidate("consumption:");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ConsumptionHistoryDto?> GetUserHistoryAsync(
        Guid? spaceId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? billingPeriodId = null,
        int pageNumber = 1,
        int pageSize = 20,
        Guid? memberUserId = null)
    {
        try
        {
            var queryParams = HttpUtility.ParseQueryString(string.Empty);
            queryParams["pageNumber"] = pageNumber.ToString();
            queryParams["pageSize"] = pageSize.ToString();

            if (spaceId.HasValue)
                queryParams["spaceId"] = spaceId.Value.ToString();

            if (startDate.HasValue)
                queryParams["startDate"] = startDate.Value.ToString("yyyy-MM-dd");

            if (endDate.HasValue)
                queryParams["endDate"] = endDate.Value.ToString("yyyy-MM-dd");

            if (billingPeriodId.HasValue)
                queryParams["billingPeriodId"] = billingPeriodId.Value.ToString();

            if (memberUserId.HasValue)
                queryParams["memberUserId"] = memberUserId.Value.ToString();

            var url = $"/api/me/consumption/history?{queryParams}";
            return await _httpClient.GetFromJsonAsync<ConsumptionHistoryDto>(url);
        }
        catch
        {
            return null;
        }
    }
}
