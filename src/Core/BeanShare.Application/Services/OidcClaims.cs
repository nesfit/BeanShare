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
    /// database user id, so sync still sees the provider's subject. Trusted only with <see cref="TransformationIssuer"/>.
    /// </summary>
    public const string ProviderSubject = "beanshare_provider_sub";

    /// <summary>
    /// The issuer of claims the claims transformation adds itself. It is distinct from the token issuers BeanShare
    /// uses — the IdP's and the API's own local JWT issuer ("BeanShare") — so a claim inside a token cannot satisfy it.
    /// </summary>
    public const string TransformationIssuer = "BeanShare.ClaimsTransformation";

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
        principal.FindAll(ProviderSubject).FirstOrDefault(c => c.Issuer == TransformationIssuer)?.Value
        ?? principal.FindFirst("sub")?.Value
        ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
