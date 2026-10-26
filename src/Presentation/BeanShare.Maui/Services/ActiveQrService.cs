using System.Net.Http.Json;
using BeanShare.Application.Features.ActiveQrCodes.Dtos;

namespace BeanShare.Maui.Services;

public class ActiveQrService : IActiveQrService
{
    private readonly HttpClient _httpClient;

    public ActiveQrService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ActiveQrCodeDto?> CreateActiveQrCodeAsync(Guid spaceId, string label, string productName, string productBrand, string productType, string recipeName, int defaultGrams, Guid? stockLotId = null)
    {
        try
        {
            var request = new
            {
                SpaceId = spaceId,
                Label = label,
                ProductName = productName,
                ProductBrand = productBrand,
                ProductType = productType,
                RecipeName = recipeName,
                DefaultGrams = defaultGrams,
                StockLotId = stockLotId
            };

            var response = await _httpClient.PostAsJsonAsync($"/api/spaces/{spaceId}/qr-codes", request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ActiveQrCodeDto>();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<ActiveQrCodeDto>?> GetActiveQrCodesAsync(Guid spaceId)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<ActiveQrCodeDto>>($"/api/spaces/{spaceId}/qr-codes");
        }
        catch
        {
            return null;
        }
    }

    public async Task<ActiveQrCodeDto?> ResolveActiveQrCodeAsync(Guid qrCodeId)
    {
        // Do NOT swallow transport failures: let HttpRequestException / TaskCanceledException
        // (timeout) propagate so callers can fall back to the offline cache and show an accurate
        // "check your connection" message. Only a real HTTP response that resolved to nothing
        // (non-success status, e.g. 400/404 for a deactivated/unknown code) returns null, which
        // callers correctly surface as "deactivated / not found".
        var response = await _httpClient.GetAsync($"/api/qr-codes/{qrCodeId}/resolve");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ActiveQrCodeDto>();
    }

    public async Task<ActiveQrCodeDto?> ReassignActiveQrCodeAsync(Guid qrCodeId, string productName, string productBrand, string productType, string recipeName, int defaultGrams, Guid? stockLotId = null)
    {
        try
        {
            var request = new
            {
                ProductName = productName,
                ProductBrand = productBrand,
                ProductType = productType,
                RecipeName = recipeName,
                DefaultGrams = defaultGrams,
                StockLotId = stockLotId
            };

            var response = await _httpClient.PutAsJsonAsync($"/api/qr-codes/{qrCodeId}/reassign", request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ActiveQrCodeDto>();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<ActiveQrCodeDto?> RenameActiveQrCodeAsync(Guid qrCodeId, string label)
    {
        try
        {
            var request = new
            {
                Label = label
            };

            var response = await _httpClient.PutAsJsonAsync($"/api/qr-codes/{qrCodeId}/rename", request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ActiveQrCodeDto>();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DeactivateActiveQrCodeAsync(Guid qrCodeId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/qr-codes/{qrCodeId}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
