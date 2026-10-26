using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using BeanShare.Application.Common;
using BeanShare.Application.Services;
using BeanShare.Contracts.Spaces;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Tests.Integration.Extensions;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BeanShare.Tests.Integration.Api;

// T-INT-09 (DS-30, U8): an identity without a user row cannot act, and sync never answers 500.
public sealed class GhostSessionTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public GhostSessionTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AGhost_CannotCreateJoinOrConfirm()
    {
        var client = _fixture.CreateClient();
        var created = await client.PostAsJsonAsync("/api/spaces", new CreateSpaceRequest { Name = "Ghost Target" });
        var invite = (await created.Content.ReadFromJsonAsync<CreateSpaceResponse>())!.InviteCode;

        client.WithTestUser(Guid.NewGuid(), "ghost@test.com");
        var create = await client.PostAsJsonAsync("/api/spaces", new CreateSpaceRequest { Name = "Ghost Space" });
        var join = await client.PostAsJsonAsync("/api/spaces/join", new JoinSpaceRequest { InviteCode = invite });
        var confirm = await client.PutAsync($"/api/settlements/{Guid.NewGuid()}/confirm-payment/{Guid.NewGuid()}", null);
        client.AsDefaultUser();

        foreach (var response in new[] { create, join, confirm })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadAsStringAsync()).Should().Contain(Error.Codes.Unauthenticated);
        }
    }

    [Fact]
    public async Task Sync_RefusedForAnUnverifiedEmail_Is403EmailNotVerified()
    {
        var client = ClientWith(s =>
        {
            s.RemoveAll<IUserSynchronizationService>();
            s.AddScoped<IUserSynchronizationService>(_ => new CannedSync(new UserSyncOutcome.Refused(Error.Codes.EmailNotVerified)));
        });

        var response = await client.PostAsync("/api/users/sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain(Error.Codes.EmailNotVerified);
    }

    [Fact]
    public async Task Sync_PendingLink_Is409()
    {
        var client = ClientWith(s =>
        {
            s.RemoveAll<IUserSynchronizationService>();
            s.AddScoped<IUserSynchronizationService>(_ => new CannedSync(new UserSyncOutcome.PlaceholderLinkPending(Guid.NewGuid(), "Old Pete")));
        });

        var response = await client.PostAsync("/api/users/sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(Error.Codes.PlaceholderLinkPending);
    }

    [Fact]
    public async Task Sync_WhoseUserLookupThrows_Is403SyncFailed_Not500()
    {
        var client = ClientWith(s =>
        {
            s.RemoveAll<IUserService>();
            s.AddScoped<IUserService, FaultingUserService>();
        });

        var response = await client.PostAsync("/api/users/sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain(Error.Codes.SyncFailed);
    }

    private HttpClient ClientWith(Action<IServiceCollection> configure) =>
        _fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(configure)).CreateClient();

    private sealed class CannedSync(UserSyncOutcome outcome) : IUserSynchronizationService
    {
        public Task<UserSyncOutcome> SyncFromClaimsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            Task.FromResult(outcome);

        public Task<LinkResolveOutcome> ResolvePlaceholderLinkAsync(ClaimsPrincipal principal, bool accept, CancellationToken cancellationToken = default) =>
            Task.FromResult(LinkResolveOutcome.NotPending);
    }

    private sealed class FaultingUserService : IUserService
    {
        private static InvalidOperationException Fault() => new("The user store is unavailable");

        public Task<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default) => throw Fault();
        public Task<User?> GetByIdForUpdateAsync(UserId userId, CancellationToken cancellationToken = default) => throw Fault();
        public Task<IReadOnlyList<User>> GetByIdsAsync(IEnumerable<UserId> userIds, CancellationToken cancellationToken = default) => throw Fault();
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) => throw Fault();
        public Task<User?> GetByProviderUserIdAsync(AuthenticationProvider provider, string providerUserId, CancellationToken cancellationToken = default) => throw Fault();
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) => throw Fault();
        public Task UpdateAsync(User user, CancellationToken cancellationToken = default) => throw Fault();
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw Fault();
        public void Evict(UserId userId, params string[] emails) => throw Fault();
    }
}
