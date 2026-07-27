using System.Net.Http.Json;
using BeanShare.Application.Features.Consumption.Queries;
using BeanShare.Application.Features.Consumption.Queries.GetBagSuggestions;
using BeanShare.Application.Features.CoffeeStock.Dtos;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.SharedUi.Components.Consumption.Models;

namespace BeanShare.Maui.Services;

/// <summary>
/// Fetches space dashboard data via the composite endpoint, reducing 4 round-trips to 1.
/// Falls back to individual service calls if the composite endpoint is unavailable.
/// </summary>
public class SpaceDashboardService
{
    private readonly HttpClient _httpClient;
    private readonly ApiResponseCache _cache;

    public SpaceDashboardService(HttpClient httpClient, ApiResponseCache cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    public async Task<SpaceDashboardData?> GetDashboardAsync(Guid spaceId)
    {
        var cacheKey = $"dashboard:{spaceId}";
        var cached = _cache.Get<SpaceDashboardData>(cacheKey);
        if (cached != null)
            return cached;

        var stale = _cache.GetStale<SpaceDashboardData>(cacheKey);

        try
        {
            var result = await _httpClient.GetFromJsonAsync<SpaceDashboardData>(
                $"/api/spaces/{spaceId}/dashboard");

            if (result != null)
                _cache.Set(cacheKey, result, TimeSpan.FromSeconds(20));

            return result;
        }
        catch
        {
            return stale;
        }
    }

    public void InvalidateDashboard(Guid spaceId)
    {
        _cache.Invalidate($"dashboard:{spaceId}");
    }
}

public class SpaceDashboardData
{
    public SpaceDashboardSpaceInfo? Space { get; set; }
    public SpaceDashboardStockInfo? Stock { get; set; }
    public GetRecentConsumptionsResult? Consumption { get; set; }
    public GetQuickPresetsResult? Presets { get; set; }

    /// <summary>Bags ranked for the log picker (open first, then by recent use).</summary>
    public BagSuggestionsResult? Bags { get; set; }
}

public class SpaceDashboardSpaceInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string InviteCode { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public bool IsActive { get; set; }
    public int MemberCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<MembershipDto> Members { get; set; } = new();
}

public class SpaceDashboardStockInfo
{
    // decimal, matching CoffeeStockDto — an int here throws on any fractional gram total.
    public decimal TotalCurrentStockGrams { get; set; }
    public decimal TotalInvestmentAmount { get; set; }
    public string TotalInvestmentCurrency { get; set; } = "";
    public List<StockLevelDto> StockLevels { get; set; } = new();
}

public class GetQuickPresetsResult
{
    public List<QuickPresetItem>? Presets { get; set; }
}

public class QuickPresetItem
{
    public Guid? GlobalPresetId { get; set; }
    public Guid? SpacePresetId { get; set; }
    public string Name { get; set; } = "";
    public string CoffeeType { get; set; } = "";
    public string Preparation { get; set; } = "";
    public decimal DefaultGrams { get; set; }
    public string? Description { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsGlobal { get; set; }
    public int DisplayOrder { get; set; }
}
