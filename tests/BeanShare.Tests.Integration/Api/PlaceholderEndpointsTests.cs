using System.Net;
using System.Net.Http.Json;
using BeanShare.Contracts.Spaces;
using BeanShare.Tests.Integration.Extensions;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;

namespace BeanShare.Tests.Integration.Api;

// T-INT-08 (placeholders, design-tables-ui §9.4): create, edit and merge, and the merged guard (W1).
public sealed class PlaceholderEndpointsTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly HttpClient _client;

    public PlaceholderEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task CreateEditMerge_AndTheMergedGuard()
    {
        var (spaceId, inviteCode) = await CreateSpaceAsync("Placeholder Lifecycle");
        var member = await JoinAsync(inviteCode);
        var email = $"{Guid.NewGuid():N}@example.org";

        var created = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/placeholders", new { Name = "Old Pete", Email = email });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var placeholder = (await created.Content.ReadFromJsonAsync<MemberRow>())!;
        placeholder.IsPlaceholder.Should().BeTrue();

        var duplicate = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/placeholders", new { Name = "Copy", Email = email.ToUpperInvariant() });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadAsStringAsync()).Should().Contain("EMAIL_IN_USE");

        var edited = await _client.PutAsJsonAsync($"/api/spaces/{spaceId}/placeholders/{placeholder.UserId}", new { Name = "Pete" });
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        (await edited.Content.ReadFromJsonAsync<MemberRow>())!.Email.Should().BeEmpty("a placeholder without an email shows none");

        var merged = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/placeholders/{placeholder.UserId}/merge", new { TargetUserId = member });
        merged.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var removed = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/members/{placeholder.UserId}/remove", new { SpaceId = spaceId, UserId = placeholder.UserId });
        removed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await removed.Content.ReadAsStringAsync()).Should().Contain("MEMBER_MERGED");
    }

    [Fact]
    public async Task Create_AsNonAdmin_IsForbidden()
    {
        var (spaceId, inviteCode) = await CreateSpaceAsync("Placeholder Non Admin");
        var member = await JoinAsync(inviteCode);

        _client.WithTestUser(member);
        var response = await _client.PostAsJsonAsync($"/api/spaces/{spaceId}/placeholders", new { Name = "Pete" });
        _client.AsDefaultUser();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(Guid SpaceId, string InviteCode)> CreateSpaceAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/spaces", new CreateSpaceRequest { Name = name });
        var created = await response.Content.ReadFromJsonAsync<CreateSpaceResponse>();
        return (created!.SpaceId, created.InviteCode);
    }

    private async Task<Guid> JoinAsync(string inviteCode)
    {
        var userId = Guid.NewGuid();
        await _fixture.EnsureUserAsync(userId, $"{userId:N}@test.com");

        _client.WithTestUser(userId, $"{userId:N}@test.com");
        var response = await _client.PostAsJsonAsync("/api/spaces/join", new JoinSpaceRequest { InviteCode = inviteCode });
        _client.AsDefaultUser();
        response.EnsureSuccessStatusCode();
        return userId;
    }

    private sealed record MemberRow(Guid UserId, string Email, bool IsPlaceholder);
}
