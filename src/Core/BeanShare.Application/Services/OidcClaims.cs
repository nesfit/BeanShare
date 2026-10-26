using System.Security.Claims;

namespace BeanShare.Application.Services;

/// <summary>Claim names and parsing shared by sync, the link page and the claims transformation (DS-29, DS-31).</summary>
public static class OidcClaims
{
    public const string Email = "email";
    public const string EmailVerified = "email_verified";

    /// <summary>Added by the web only when sync answers PlaceholderLinkPending; never trusted from the IdP (X6).</summary>
    public const string LinkPending = "beanshare_link_pending";

    /// <summary>
    /// The identity provider's own subject, kept by the claims transformation when it replaces <c>sub</c> with the
    /// database user id, so sync still sees the provider's subject. Trusted only with <see cref="LocalIssuer"/>.
    /// </summary>
    public const string ProviderSubject = "beanshare_provider_sub";

    /// <summary>The issuer of claims BeanShare adds itself; a token from the IdP never carries it.</summary>
    public const string LocalIssuer = "BeanShare";

    /// <summary>The verified flag as a bool, ignoring case ("True", "true"); anything else is false (N5).</summary>
    public static bool IsEmailVerified(ClaimsPrincipal principal) =>
        principal.FindAll(EmailVerified).Any(c => bool.TryParse(c.Value, out var verified) && verified);

    public static string? EmailOf(ClaimsPrincipal principal)
    {
        var email = principal.FindFirst(Email)?.Value;
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    public static bool HasLinkPending(ClaimsPrincipal principal) => principal.HasClaim(c => c.Type == LinkPending);

    /// <summary>
    /// The provider's subject: the one the claims transformation kept, else <c>sub</c>, else the name identifier.
    /// </summary>
    public static string? ProviderSubjectOf(ClaimsPrincipal principal) =>
        principal.FindAll(ProviderSubject).FirstOrDefault(c => c.Issuer == LocalIssuer)?.Value
        ?? principal.FindFirst("sub")?.Value
        ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
