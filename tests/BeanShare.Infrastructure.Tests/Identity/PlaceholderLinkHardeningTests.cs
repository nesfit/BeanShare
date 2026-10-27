using System.Security.Claims;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Infrastructure.Identity;
using BeanShare.Infrastructure.Tests.InMemory;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using UserEntity = BeanShare.Domain.Entities.User;

namespace BeanShare.Infrastructure.Tests.Identity;

/// <summary>
/// Sync, the placeholder link and the claims transformation together (InMemory, real services). The API runs the
/// transformation before every endpoint, so sync must still see the provider's subject, and a sentinel address
/// never links or maps anything.
/// </summary>
[Collection(InMemoryContainerCollection.Name)]
public sealed class PlaceholderLinkHardeningTests
{
    private static ClaimsPrincipal Principal(string sub, string email, string? verified) =>
        new(new ClaimsIdentity(
            new[] { new Claim("sub", sub), new Claim("name", "Eve"), new Claim(OidcClaims.Email, email) }
                .Concat(verified is null ? [] : new[] { new Claim(OidcClaims.EmailVerified, verified) }),
            "Bearer"));

    private static async Task<UserEntity> SeedAsync(InMemoryContainer c, UserEntity user)
    {
        await c.ReadAsync(async db =>
        {
            db.Users.Add(user);
            return await db.SaveChangesAsync();
        });
        return user;
    }

    private static Task<UserEntity> SeedOidcUserAsync(InMemoryContainer c, string email, string sub) =>
        SeedAsync(c, UserEntity.CreateWithProvider(email, "Eve", AuthenticationProvider.Oidc, sub, DateTime.UtcNow));

    /// <summary>What the API does before every endpoint.</summary>
    private static async Task<ClaimsPrincipal> ApiTransformAsync(InMemoryContainer c, ClaimsPrincipal principal)
    {
        await using var scope = c.Services.CreateAsyncScope();
        var transformation = new OidcClaimsTransformation(
            scope.ServiceProvider.GetRequiredService<IUserService>(), scope.ServiceProvider.GetRequiredService<IMemoryCache>());
        return await transformation.TransformAsync(principal);
    }

    private static async Task<UserSyncOutcome> SyncAsync(InMemoryContainer c, ClaimsPrincipal principal)
    {
        await using var scope = c.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<UserSynchronizationService>(scope.ServiceProvider).SyncFromClaimsAsync(principal);
    }

    private static async Task<LinkResolveOutcome> ResolveAsync(InMemoryContainer c, ClaimsPrincipal principal, bool accept)
    {
        await using var scope = c.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<UserSynchronizationService>(scope.ServiceProvider)
            .ResolvePlaceholderLinkAsync(principal, accept);
    }

    private static Task<UserEntity> ReloadAsync(InMemoryContainer c, UserId id) =>
        c.ReadAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == id));

    private static async Task<(InMemoryContainer C, UserId Placeholder)> PlaceholderAsync(string? email)
    {
        var c = await InMemoryContainer.CreateAsync();
        var admin = await c.SeedUserAsync("admin@example.org", "Admin");
        var space = await c.SendAsync(admin, new CreateSpaceCommand("Kava", "CZK"));
        var placeholder = await c.SendAsync(admin, new CreatePlaceholderMemberCommand(space.Value.SpaceId, "Old Pete", email));
        placeholder.IsSuccess.Should().BeTrue();
        return (c, new UserId(placeholder.Value.UserId));
    }

    // ---- The provider subject survives the API's transformation ----

    [Fact(Timeout = 20000)]
    public async Task AnApiSync_AfterTheTransformation_KeepsTheProviderLink()
    {
        await using var c = await InMemoryContainer.CreateAsync();
        var eve = await SeedOidcUserAsync(c, "eve@example.org", "kc-sub-eve");

        var principal = await ApiTransformAsync(c, Principal("kc-sub-eve", "eve@example.org", "true"));
        principal.FindFirst("sub")!.Value.Should().Be(eve.Id.Value.ToString(), "the API maps sub to the database id");
        OidcClaims.ProviderSubjectOf(principal).Should().Be("kc-sub-eve");

        (await SyncAsync(c, principal)).Should().BeOfType<UserSyncOutcome.Synced>().Which.User.Id.Should().Be(eve.Id);
        (await ReloadAsync(c, eve.Id)).ProviderUserId.Should().Be("kc-sub-eve");
    }

    [Fact(Timeout = 20000)]
    public async Task AnApiSync_OfASubjectLinkedUserWithAnUnverifiedEmail_IsSynced()
    {
        await using var c = await InMemoryContainer.CreateAsync();
        var eve = await SeedOidcUserAsync(c, "eve@example.org", "kc-sub-eve");

        var outcome = await SyncAsync(c, await ApiTransformAsync(c, Principal("kc-sub-eve", "eve@example.org", "false")));

        outcome.Should().BeOfType<UserSyncOutcome.Synced>("the account is linked by subject; no email linking is involved")
            .Which.User.Id.Should().Be(eve.Id);
    }

    [Fact(Timeout = 20000)]
    public async Task AfterAnApiSync_AnEmailChangeAtTheProvider_StillFindsTheSameAccount()
    {
        await using var c = await InMemoryContainer.CreateAsync();
        var eve = await SeedOidcUserAsync(c, "eve@example.org", "kc-sub-eve");
        (await SyncAsync(c, await ApiTransformAsync(c, Principal("kc-sub-eve", "eve@example.org", "true")))).Should().BeOfType<UserSyncOutcome.Synced>();

        // The web syncs the raw principal; the provider changed Eve's email meanwhile.
        var web = await SyncAsync(c, Principal("kc-sub-eve", "eve.new@example.org", "true"));

        web.Should().BeOfType<UserSyncOutcome.Synced>().Which.User.Id.Should().Be(eve.Id);
        (await c.ReadAsync(db => db.Users.CountAsync())).Should().Be(1, "no second account is created");
    }

    [Fact(Timeout = 20000)]
    public async Task AnEmailMatch_NeverReplacesAnExistingProviderLink()
    {
        await using var c = await InMemoryContainer.CreateAsync();
        var eve = await SeedOidcUserAsync(c, "eve@example.org", "kc-old");

        var outcome = await SyncAsync(c, Principal("kc-new", "eve@example.org", "true"));

        outcome.Should().BeOfType<UserSyncOutcome.Synced>().Which.User.Id.Should().Be(eve.Id);
        (await ReloadAsync(c, eve.Id)).ProviderUserId.Should().Be("kc-old");
    }

    [Fact(Timeout = 20000)]
    public async Task AnEmailMatch_StillLinksAnAccountWithoutAProviderLink()
    {
        await using var c = await InMemoryContainer.CreateAsync();
        var anna = await SeedAsync(c, UserEntity.CreateWithIdAndPassword(Guid.NewGuid(), "anna@example.org", "Anna", "hash", DateTime.UtcNow));

        (await SyncAsync(c, Principal("kc-anna", "anna@example.org", "true"))).Should().BeOfType<UserSyncOutcome.Synced>();

        (await ReloadAsync(c, anna.Id)).ProviderUserId.Should().Be("kc-anna");
    }

    [Fact]
    public void AProviderSubjectClaimFromTheIdP_IsNotTrusted()
    {
        var principal = Principal("kc-real", "eve@example.org", "true");
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(OidcClaims.ProviderSubject, "kc-someone-else", ClaimValueTypes.String, "https://idp.example.org"));

        OidcClaims.ProviderSubjectOf(principal).Should().Be("kc-real");
    }

    [Fact]
    public void AProviderSubjectClaimFromALocalLoginToken_IsNotTrusted()
    {
        // The API's local JWT tokens are issued as Jwt:Issuer; a claim stamped with that issuer must not pass.
        var localIssuer = LocalJwtIssuer();
        localIssuer.Should().NotBe(OidcClaims.TransformationIssuer);
        var principal = Principal("local-sub", "eve@example.org", "true");
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(OidcClaims.ProviderSubject, "kc-someone-else", ClaimValueTypes.String, localIssuer));

        OidcClaims.ProviderSubjectOf(principal).Should().Be("local-sub");
    }

    private static string LocalJwtIssuer()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeanShare.slnf")))
            directory = directory.Parent;
        var appsettings = Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("BeanShare.slnf not found above the test output"),
            "src", "Presentation", "BeanShare.Api", "appsettings.json");
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(appsettings));
        return json.RootElement.GetProperty("Jwt").GetProperty("Issuer").GetString()!;
    }

    // ---- Sentinel addresses never link or map ----

    [Fact(Timeout = 20000)]
    public async Task SentinelEmails_AreRefusedEverywhere()
    {
        var (c, placeholder) = await PlaceholderAsync(null);
        await using var _ = c;
        var sentinel = UserEntity.SentinelEmailFor(placeholder);

        (await SyncAsync(c, Principal("idp-x", sentinel, "true"))).Should().BeOfType<UserSyncOutcome.Refused>();
        (await SyncAsync(c, Principal("idp-x", sentinel.ToUpperInvariant(), "true"))).Should().BeOfType<UserSyncOutcome.Refused>();
        (await ResolveAsync(c, Principal("idp-x", sentinel, "true"), accept: true)).Should().Be(LinkResolveOutcome.NotPending);
        (await ResolveAsync(c, await ApiTransformAsync(c, Principal("idp-y", sentinel.ToUpperInvariant(), "true")), accept: false))
            .Should().Be(LinkResolveOutcome.NotPending);

        var unchanged = await ReloadAsync(c, placeholder);
        unchanged.IsPlaceholder.Should().BeTrue();
        unchanged.ProviderUserId.Should().BeNull();
        (await c.ReadAsync(db => db.Users.CountAsync())).Should().Be(2, "the admin and the placeholder; no account was created");
    }

    [Fact(Timeout = 20000)]
    public async Task TheTransformation_NeverMapsASentinelAddress()
    {
        await using var c = await InMemoryContainer.CreateAsync();
        // An account that still carries a sentinel address (e.g. claimed before the sentinel refusal).
        var legacy = UserEntity.CreatePlaceholder("Old Pete", null, DateTime.UtcNow);
        legacy.ClaimPlaceholder(AuthenticationProvider.Oidc, "kc-pete", DateTime.UtcNow);
        await SeedAsync(c, legacy);

        var principal = await ApiTransformAsync(c, Principal("kc-mallory", legacy.Email.ToUpperInvariant(), "true"));

        principal.FindFirst("sub")!.Value.Should().Be("kc-mallory");
        OidcClaims.ProviderSubjectOf(principal).Should().Be("kc-mallory");
    }

    // ---- A sign-in that already has an account cannot answer a link ----

    [Fact(Timeout = 20000)]
    public async Task Resolve_ForASignInThatAlreadyHasAnAccount_IsNotPending()
    {
        var (c, placeholder) = await PlaceholderAsync("new@example.org");
        await using var _ = c;
        await SeedOidcUserAsync(c, "old@example.org", "kc-sub-p");

        (await ResolveAsync(c, Principal("kc-sub-p", "new@example.org", "true"), accept: true)).Should().Be(LinkResolveOutcome.NotPending);
        (await ResolveAsync(c, Principal("kc-sub-p", "new@example.org", "true"), accept: false)).Should().Be(LinkResolveOutcome.NotPending);

        var unchanged = await ReloadAsync(c, placeholder);
        unchanged.IsPlaceholder.Should().BeTrue();
        unchanged.Email.Should().Be("new@example.org");
    }
}
