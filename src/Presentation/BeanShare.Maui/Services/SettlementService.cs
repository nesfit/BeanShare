using System.Net.Http.Json;
using BeanShare.Contracts.Settlement;

namespace BeanShare.Maui.Services;

public class SettlementService : ISettlementService
{
    private readonly HttpClient _httpClient;
    private readonly ApiResponseCache _cache;

    public SettlementService(HttpClient httpClient, ApiResponseCache cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    public async Task<List<SettlementSummaryDto>> GetSpaceSettlementsAsync(Guid spaceId)
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<List<SettlementSummaryDto>>($"/api/spaces/{spaceId}/settlements");
            return result ?? new List<SettlementSummaryDto>();
        }
        catch
        {
            return new List<SettlementSummaryDto>();
        }
    }

    public async Task<SettlementDto?> GetSettlementByIdAsync(Guid settlementId)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<SettlementDto>($"/api/settlements/{settlementId}");
        }
        catch
        {
            return null;
        }
    }

    public async Task<SettlementDto?> GenerateSettlementAsync(Guid billingPeriodId)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/billing-periods/{billingPeriodId}/settlement", new { });
            response.EnsureSuccessStatusCode();
            _cache.Invalidate("consumption:"); // generation stamps late cups, which changes their flags
            _cache.Invalidate("dashboard:"); // the dashboard carries the same recent list
            return await response.Content.ReadFromJsonAsync<SettlementDto>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> ConfirmPaymentAsync(Guid settlementId, Guid memberUserId)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"/api/settlements/{settlementId}/confirm-payment/{memberUserId}", new { });
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<byte[]?> ExportPdfAsync(Guid settlementId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/settlements/{settlementId}/export/pdf");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            return null;
        }
    }

    public async Task<byte[]?> ExportExcelAsync(Guid settlementId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/settlements/{settlementId}/export/excel");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            return null;
        }
    }

    public async Task<int> SendEmailsAsync(Guid settlementId, bool attachPdf = true)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/settlements/{settlementId}/send-emails", new { AttachPdf = attachPdf });
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SendEmailsResponse>();
            return result?.EmailsSent ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private record SendEmailsResponse(int EmailsSent, string Message);
}
