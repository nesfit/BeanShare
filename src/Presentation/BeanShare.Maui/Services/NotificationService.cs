using System.Net.Http.Json;
using BeanShare.Contracts.Notifications;

namespace BeanShare.Maui.Services;

public class NotificationService : INotificationService
{
    private readonly HttpClient _httpClient;

    public NotificationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<NotificationListResponse?> GetNotificationsAsync(bool unreadOnly = false)
    {
        try
        {
            var url = unreadOnly ? "/api/me/notifications?unreadOnly=true" : "/api/me/notifications";
            return await _httpClient.GetFromJsonAsync<NotificationListResponse>(url);
        }
        catch
        {
            return null;
        }
    }

    public async Task<int> GetUnreadCountAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<UnreadCountResponse>("/api/me/notifications/unread-count");
            return response?.Count ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/me/notifications/{notificationId}/read", new { });
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> MarkAllAsReadAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/me/notifications/read-all", null);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<MarkAsReadResponse>();
                return result?.MarkedCount ?? 0;
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<int> ClearAllAsync()
    {
        try
        {
            var response = await _httpClient.DeleteAsync("/api/me/notifications");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ClearNotificationsResponse>();
                return result?.DeletedCount ?? 0;
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }
}
