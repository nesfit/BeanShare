using System.Security.Claims;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace BeanShare.Infrastructure.Identity;

/// <summary>
/// Remaps the OIDC "sub" claim to the database user ID when they differ,
/// and adds database-backed system role claims. This supports both GUID-based
/// and opaque provider subject identifiers.
/// </summary>
public sealed class OidcClaimsTransformation : IClaimsTransformation
{
    private readonly IUserService _userService;
    private readonly IMemoryCache _cache;
    private readonly UserSyncSettings _settings;

    private sealed record CachedUserResult(Guid DbUserId, SystemRole SystemRole);

    public OidcClaimsTransformation(IUserService userService, IMemoryCache cache, UserSyncSettings? settings = null)
    {
        _userService = userService;
        _cache = cache;
        _settings = settings ?? new UserSyncSettings();
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        var identity = principal.Identity as ClaimsIdentity;
        if (identity is null)
            return principal;

        if (identity.HasClaim("beanshare_transformed", "true"))
            return principal;

        var sub = principal.FindFirst("sub")?.Value
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(sub))
            return principal;

        var identityProvider = principal.FindFirst("identity_provider")?.Value ?? "oidc";
        var provider = identityProvider.ToLowerInvariant() switch
        {
            "google" => AuthenticationProvider.Google,
            "facebook" => AuthenticationProvider.Facebook,
            _ => AuthenticationProvider.Oidc
        };

        var userByProvider = await _userService.GetByProviderUserIdAsync(provider, sub);

        // The email fallback needs a verified address, never maps to a placeholder and never uses a
        // sentinel address: claiming a placeholder happens only through sync and the person's acceptance.
        var email = principal.FindFirst("email")?.Value?.Trim();
        User? userByEmail = null;
        if (userByProvider is null && !string.IsNullOrEmpty(email) && !User.IsSentinelEmail(email)
            && (OidcClaims.IsEmailVerified(principal) || !_settings.RequireVerifiedEmailForLinking))
        {
            var candidate = await _userService.GetByEmailAsync(email);
            if (candidate is { IsPlaceholder: false, IsMerged: false })
                userByEmail = candidate;
        }

        var user = userByProvider ?? userByEmail;

        if (user is null)
        {
            identity.AddClaim(new Claim("beanshare_transformed", "true"));
            return principal;
        }

        if (!string.Equals(sub, user.Id.Value.ToString(), StringComparison.Ordinal))
        {
            // Keep the provider's subject for sync, which must find the account by it, not by the database id.
            identity.AddClaim(new Claim(OidcClaims.ProviderSubject, sub, ClaimValueTypes.String, OidcClaims.TransformationIssuer));

            var oldSub = identity.FindFirst("sub");
            if (oldSub is not null)
                identity.RemoveClaim(oldSub);
            identity.AddClaim(new Claim("sub", user.Id.Value.ToString()));

            var oldNameId = identity.FindFirst(ClaimTypes.NameIdentifier);
            if (oldNameId is not null)
                identity.RemoveClaim(oldNameId);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.Value.ToString()));
        }

        if (user.SystemRole == SystemRole.SystemAdmin && !principal.IsInRole("admin"))
            identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));

        identity.AddClaim(new Claim("beanshare_transformed", "true"));
        return principal;
    }
}
