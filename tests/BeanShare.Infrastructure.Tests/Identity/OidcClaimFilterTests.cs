using System.Security.Claims;
using BeanShare.Application.Services;
using BeanShare.Infrastructure.Identity;
using FluentAssertions;

namespace BeanShare.Infrastructure.Tests.Identity;

// The cookie claim filter keeps what sync and the link page read, and the pending claim
// can only come from sync.
public sealed class OidcClaimFilterTests
{
    private static ClaimsIdentity IdpIdentity(params Claim[] extra) => new(new[]
    {
        new Claim("sub", "s"), new Claim(ClaimTypes.NameIdentifier, "s"), new Claim("email", "pete@example.org"),
        new Claim("preferred_username", "pete"), new Claim("name", "Pete"), new Claim("identity_provider", "oidc"),
        new Claim(ClaimTypes.Role, "user"), new Claim("email_verified", "true"),
        new Claim("iss", "https://idp"), new Claim("nonce", "n"), new Claim("realm_access", "{}"), new Claim("picture", "p"),
    }.Concat(extra), "oidc");

    [Fact]
    public void KeepEssential_KeepsTheVerifiedFlagAndThePendingClaim_AndDropsTheRest()
    {
        var identity = IdpIdentity(new Claim(OidcClaims.LinkPending, "true"));

        OidcClaimFilter.KeepEssential(identity);

        identity.Claims.Select(c => c.Type).Should().BeEquivalentTo(
            "sub", ClaimTypes.NameIdentifier, "email", "preferred_username", "name", "identity_provider",
            ClaimTypes.Role, "email_verified", OidcClaims.LinkPending);
    }

    [Fact]
    public void RemoveLinkPending_DropsAnIdpSuppliedClaimFromEveryIdentity()
    {
        var first = IdpIdentity(new Claim(OidcClaims.LinkPending, "true"));
        var second = new ClaimsIdentity(new[] { new Claim(OidcClaims.LinkPending, "1"), new Claim(OidcClaims.LinkPending, "2") });
        var principal = new ClaimsPrincipal(new[] { first, second });

        OidcClaimFilter.RemoveLinkPending(principal);

        OidcClaims.HasLinkPending(principal).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void TheWebFlow_LeavesExactlyThePendingClaimThatSyncAdded(bool syncSaysPending, int expected)
    {
        // Mirrors BlazorWeb's OnTokenValidated: strip the IdP's claim, sync, add one claim if pending, filter.
        var identity = IdpIdentity(new Claim(OidcClaims.LinkPending, "forged"));
        var principal = new ClaimsPrincipal(identity);

        OidcClaimFilter.RemoveLinkPending(principal);
        if (syncSaysPending)
            identity.AddClaim(new Claim(OidcClaims.LinkPending, "true"));
        OidcClaimFilter.KeepEssential(identity);

        principal.FindAll(OidcClaims.LinkPending).Should().HaveCount(expected);
        OidcClaims.IsEmailVerified(principal).Should().BeTrue();
    }
}
