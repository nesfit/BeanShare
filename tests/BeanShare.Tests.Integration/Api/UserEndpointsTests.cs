using System.Net;
using System.Net.Http.Json;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;

namespace BeanShare.Tests.Integration.Api;

public sealed class UserEndpointsTests : IClassFixture<PostgreSqlFixture>
{
    private readonly HttpClient _client;

    public UserEndpointsTests(PostgreSqlFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task GetCurrentUser_ShouldReturnOk()
    {
        var response = await _client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetCurrentUser_ShouldContainUserId()
    {
        var response = await _client.GetAsync("/api/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("UserId");
    }

    [Fact]
    public async Task UpdateProfile_WithValidRequest_ShouldReturnOkOrBadRequest()
    {
        var request = new { Name = "Updated Name", PictureUrl = (string?)null };

        var response = await _client.PutAsJsonAsync("/api/me/profile", request);

        // The fixture seeds user 1111…, so the profile exists (U2).
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdatePreferredCurrency_WithValidCurrency_ShouldReturnOkOrBadRequest()
    {
        var request = new { CurrencyCode = "EUR" };

        var response = await _client.PutAsJsonAsync("/api/me/preferred-currency", request);

        // The fixture seeds user 1111…, so the profile exists (U2).
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SyncUser_WithMockAuth_IsRefusedWithSyncFailed()
    {
        // The mock principal has no OIDC "email" claim, so sync cannot match or create anyone: the endpoint
        // refuses with 403 SYNC_FAILED instead of throwing (DS-30).
        var response = await _client.PostAsync("/api/users/sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SYNC_FAILED");
    }

    [Fact]
    public async Task UserWorkflow_GetProfileAndStats_ShouldWork()
    {
        // Get profile (uses MockUserContext which always returns a valid user)
        var meResponse = await _client.GetAsync("/api/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get stats (requires X-User-Id header for the endpoint)
        _client.DefaultRequestHeaders.Remove("X-User-Id");
        _client.DefaultRequestHeaders.Add("X-User-Id", "11111111-1111-1111-1111-111111111111");
        var statsResponse = await _client.GetAsync("/api/me/statistics");
        statsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        _client.DefaultRequestHeaders.Remove("X-User-Id");
    }
}
