using System.Net;
using System.Net.Http.Json;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Contracts.Spaces;
using BeanShare.Tests.Integration.Extensions;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;

namespace BeanShare.Tests.Integration.Api;

// The money-ledger endpoints answer with the expected status codes, refuse non-admins and
// show members only their own entries.
public sealed class LedgerEndpointsTests : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid DefaultUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly PostgreSqlFixture _fixture;
    private readonly HttpClient _client;

    public LedgerEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task GetBalances_AsAdmin_ReturnsOk()
    {
        var (spaceId, _) = await CreateSpaceAsync("Ledger Balances");

        var response = await _client.GetAsync($"/api/spaces/{spaceId}/balances");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var balances = await response.Content.ReadFromJsonAsync<SpaceBalancesDto>();
        balances!.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task EntryLifecycle_CreateUpdateDelete_ReturnsCreatedOkNoContent()
    {
        var (spaceId, inviteCode) = await CreateSpaceAsync("Ledger Entry Lifecycle");
        var member = await JoinAsync(inviteCode);

        var created = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/balance-entries",
            new { UserId = member, Amount = -25m, EffectiveDate = Today, Note = "Excel cutoff" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var entry = await created.Content.ReadFromJsonAsync<BalanceEntryDto>();

        var updated = await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/balance-entries/{entry!.Id}",
            new { UserId = member, CounterpartyUserId = DefaultUserId, Amount = -30m, EffectiveDate = Today, Note = "Excel cutoff", Kind = "Correction" });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleted = await _client.DeleteAsync($"/api/spaces/{spaceId}/balance-entries/{entry.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreateEntry_AsNonAdmin_IsForbidden()
    {
        var (spaceId, inviteCode) = await CreateSpaceAsync("Ledger Non Admin");
        var member = await JoinAsync(inviteCode);

        _client.WithTestUser(member);
        var response = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/balance-entries",
            new { UserId = member, Amount = 25m, EffectiveDate = Today, Note = "self-service" });
        _client.AsDefaultUser();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBalances_AsMember_ShowsOnlyTheirOwnEntries()
    {
        var (spaceId, inviteCode) = await CreateSpaceAsync("Ledger Visibility");
        var alice = await JoinAsync(inviteCode);
        var bob = await JoinAsync(inviteCode);
        (await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/balance-entries",
            new { UserId = alice, Amount = -10m, EffectiveDate = Today, Note = "alice" })).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/balance-entries",
            new { UserId = bob, Amount = -20m, EffectiveDate = Today, Note = "bob" })).EnsureSuccessStatusCode();

        _client.WithTestUser(alice);
        var response = await _client.GetAsync($"/api/spaces/{spaceId}/balances");
        _client.AsDefaultUser();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var balances = await response.Content.ReadFromJsonAsync<SpaceBalancesDto>();
        balances!.IsAdmin.Should().BeFalse();
        balances.Entries.Should().ContainSingle().Which.Note.Should().Be("alice");
        balances.Members.Should().ContainSingle().Which.UserId.Should().Be(alice);
    }

    [Fact]
    public async Task Paste_PreviewAndCommit_ReturnOkAndCreated()
    {
        var (spaceId, inviteCode) = await CreateSpaceAsync("Ledger Paste");
        var member = await JoinAsync(inviteCode);
        var request = new { Text = $"{EmailOf(member)};-12.50;Excel", BatchNote = "cutover" };
        var unknown = new { Text = "nobody@test.com;-12.50;Excel" };

        var preview = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/balance-entries/paste/preview", request);
        var commit = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/balance-entries/paste", request);
        var refused = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/balance-entries/paste", unknown);

        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        (await preview.Content.ReadFromJsonAsync<BalancePastePreviewDto>())!.ValidRowCount.Should().Be(1);
        commit.StatusCode.Should().Be(HttpStatusCode.Created);
        (await commit.Content.ReadFromJsonAsync<List<BalanceEntryDto>>()).Should().ContainSingle()
            .Which.Amount.Should().Be(-12.50m);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LedgerSettings_UpdateAndImpact_ReturnOk_ButNotForMembers()
    {
        var (spaceId, inviteCode) = await CreateSpaceAsync("Ledger Settings");
        var member = await JoinAsync(inviteCode);

        var update = await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/ledger-settings",
            new { TreasurerUserId = DefaultUserId, LedgerStartDate = Today });
        var impact = await _client.GetAsync($"/api/spaces/{spaceId}/ledger-settings/impact?ledgerStartDate={Today:yyyy-MM-dd}");
        _client.WithTestUser(member);
        var refused = await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/ledger-settings", new { ClearTreasurer = true });
        _client.AsDefaultUser();

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await update.Content.ReadFromJsonAsync<LedgerSettingsDto>())!.TreasurerUserId.Should().Be(DefaultUserId);
        impact.StatusCode.Should().Be(HttpStatusCode.OK);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SettlementPreviewAndPeriodDelete_ForAnUnknownPeriod_ReturnNotFound()
    {
        var preview = await _client.GetAsync($"/api/billing-periods/{Guid.NewGuid()}/settlement/preview");
        var delete = await _client.DeleteAsync($"/api/billing-periods/{Guid.NewGuid()}");

        preview.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletePeriod_ForADraft_ReturnsNoContent()
    {
        var (spaceId, _) = await CreateSpaceAsync("Ledger Draft Delete");
        var created = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/billing-periods",
            new { SpaceId = spaceId, Name = "Draft", StartDate = Today, EndDate = Today.AddDays(30) });
        created.EnsureSuccessStatusCode();
        var periodId = (await _client.GetFromJsonAsync<List<PeriodRow>>($"/api/spaces/{spaceId}/billing-periods"))!.Single().Id;

        var delete = await _client.DeleteAsync($"/api/billing-periods/{periodId}");

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static DateTime Today => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    private async Task<(Guid SpaceId, string InviteCode)> CreateSpaceAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/spaces", new CreateSpaceRequest { Name = name });
        var created = await response.Content.ReadFromJsonAsync<CreateSpaceResponse>();
        return (created!.SpaceId, created.InviteCode);
    }

    /// <summary>A new user (with the profile row every command needs) joins the space.</summary>
    private async Task<Guid> JoinAsync(string inviteCode)
    {
        var userId = Guid.NewGuid();
        await _fixture.EnsureUserAsync(userId, EmailOf(userId));

        _client.WithTestUser(userId, EmailOf(userId));
        var response = await _client.PostAsJsonAsync("/api/spaces/join", new JoinSpaceRequest { InviteCode = inviteCode });
        _client.AsDefaultUser();
        response.EnsureSuccessStatusCode();
        return userId;
    }

    private static string EmailOf(Guid userId) => $"{userId:N}@test.com";

    private sealed record PeriodRow(Guid Id);
}
