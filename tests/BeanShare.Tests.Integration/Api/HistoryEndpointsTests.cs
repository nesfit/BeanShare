using System.Net;
using System.Net.Http.Json;
using BeanShare.Contracts.Spaces;
using BeanShare.Tests.Integration.Extensions;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;

namespace BeanShare.Tests.Integration.Api;

// Preview, commit, read and delete an Excel history import.
public sealed class HistoryEndpointsTests : IClassFixture<PostgreSqlFixture>
{
    private const string Cups = "period;start;end;member;cups\n2025 H1;2025-01-01;2025-06-30;Old Pete;120\n2025 H1;2025-01-01;2025-06-30;test@coffeespace.local;80";
    private const string Purchases = "2025 H1;;;Beans;1000;450";

    private readonly PostgreSqlFixture _fixture;
    private readonly HttpClient _client;

    public HistoryEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task PreviewCommitReadDelete()
    {
        var spaceId = await CreateSpaceAsync("History Lifecycle");
        (await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/ledger-settings", new { LedgerStartDate = Today })).EnsureSuccessStatusCode();
        var request = new { CupsText = Cups, PurchasesText = Purchases, Note = "Excel", CreateMissingPlaceholders = true };

        var preview = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/history-imports/preview", request);
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        (await preview.Content.ReadFromJsonAsync<PreviewRow>())!.TotalCups.Should().Be(200);

        var commit = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/history-imports", request);
        commit.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await commit.Content.ReadFromJsonAsync<ResultRow>())!;
        result.PeriodCount.Should().Be(1);

        var history = await _client.GetAsync($"/api/spaces/{spaceId}/history");
        history.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleted = await _client.DeleteAsync($"/api/spaces/{spaceId}/history-imports/{result.ImportId}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Commit_WithoutALedgerStart_IsABadRequest_AndANonAdminIsForbidden()
    {
        var spaceId = await CreateSpaceAsync("History Refused");
        var request = new { CupsText = Cups, CreateMissingPlaceholders = true };

        var refused = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/history-imports", request);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("HISTORY_IMPORT_INVALID");

        _client.WithTestUser(FixtureUsers.SecondUserId);
        var forbidden = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/history-imports/preview", request);
        _client.AsDefaultUser();
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static DateTime Today => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private async Task<Guid> CreateSpaceAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/spaces", new CreateSpaceRequest { Name = name });
        return (await response.Content.ReadFromJsonAsync<CreateSpaceResponse>())!.SpaceId;
    }

    private sealed record PreviewRow(int TotalCups);

    private sealed record ResultRow(Guid ImportId, int PeriodCount);
}
