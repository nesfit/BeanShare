using System.Security.Claims;
using BeanShare.Application.Services;

namespace BeanShare.Infrastructure.Identity;

/// <summary>
/// What the web keeps of an OIDC principal. Extracted from BlazorWeb's token-validated handler so
/// it can be tested: the cookie must stay small (nginx header limits), but it must keep the claims sync
/// and the link page read.
/// </summary>
public static class OidcClaimFilter
{
    /// <summary>The claim types the application reads after sign-in; everything else is dropped.</summary>
    public static readonly IReadOnlySet<string> EssentialClaimTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "sub",
        ClaimTypes.NameIdentifier,
        "email",
        "preferred_username",
        "name",
        "identity_provider",
        ClaimTypes.Role,
        OidcClaims.EmailVerified,
        OidcClaims.LinkPending,
    };

    /// <summary>
    /// Removes the non-essential claims (JWT and protocol fields, raw role blobs, extended profile fields)
    /// so the auth cookie does not chunk into several browser cookies.
    /// </summary>
    public static void KeepEssential(ClaimsIdentity identity)
    {
        foreach (var claim in identity.Claims.Where(c => !EssentialClaimTypes.Contains(c.Type)).ToList())
            identity.RemoveClaim(claim);
    }

    /// <summary>
    /// Drops every pending-link claim the identity provider may have sent; only a PlaceholderLinkPending
    /// outcome of sync adds one back.
    /// </summary>
    public static void RemoveLinkPending(ClaimsPrincipal principal)
    {
        foreach (var identity in principal.Identities)
        {
            foreach (var claim in identity.FindAll(OidcClaims.LinkPending).ToList())
                identity.RemoveClaim(claim);
        }
    }
}
