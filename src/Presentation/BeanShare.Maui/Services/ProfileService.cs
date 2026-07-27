using System.Net.Http.Json;
using BeanShare.Contracts.Me;
using BeanShare.Contracts.Users;

namespace BeanShare.Maui.Services;

public sealed class ProfileService : IProfileService
{
    private readonly HttpClient _httpClient;

    public ProfileService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> GetRevolutTagAsync()
    {
        try
        {
            var me = await _httpClient.GetFromJsonAsync<CurrentUserResponse>("/api/me");
            return me?.RevolutTag;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SetRevolutTagAsync(string? revolutTag)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(
                "/api/me/revolut-tag",
                new SetRevolutTagRequest { RevolutTag = revolutTag });
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
