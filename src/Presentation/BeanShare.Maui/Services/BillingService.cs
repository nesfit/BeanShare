using System.Net.Http.Json;
using BeanShare.Contracts.Billing;

namespace BeanShare.Maui.Services;

public class BillingService : IBillingService
{
    private readonly HttpClient _httpClient;

    public BillingService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<BillingPeriodSummaryDto>> GetSpaceBillingPeriodsAsync(Guid spaceId)
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<BillingPeriodSummaryDto>>($"/api/spaces/{spaceId}/billing-periods");
            return result ?? new List<BillingPeriodSummaryDto>();
        }
        catch
        {
            return new List<BillingPeriodSummaryDto>();
        }
    }

    public async Task<BillingPeriodDto?> GetBillingPeriodByIdAsync(Guid billingPeriodId)
    {
        var response = await _httpClient.GetAsync($"/api/billing-periods/{billingPeriodId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<BillingPeriodDto>();
    }

    public async Task<BillingPeriodDto?> CreateBillingPeriodAsync(Guid spaceId, CreateBillingPeriodDto dto)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/spaces/{spaceId}/billing-periods", dto);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BillingPeriodDto>();
        }
        catch
        {
            return null;
        }
    }

    public async Task OpenBillingPeriodAsync(Guid billingPeriodId)
    {
        using var response = await _httpClient.PutAsJsonAsync($"/api/billing-periods/{billingPeriodId}/open", new { });
        response.EnsureSuccessStatusCode();
    }

    public async Task CloseBillingPeriodAsync(Guid billingPeriodId)
    {
        using var response = await _httpClient.PutAsJsonAsync($"/api/billing-periods/{billingPeriodId}/close", new { });
        response.EnsureSuccessStatusCode();
    }
}
