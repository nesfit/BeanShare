using System.Security.Claims;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using Microsoft.Extensions.Logging;

namespace BeanShare.Application.Services;

/// <summary>
/// Service for synchronizing user data from an OIDC provider to the local database.
/// Creates or updates user records based on OIDC claims.
/// </summary>
public interface IUserSynchronizationService
{
    /// <summary>
    /// Synchronizes a user from OIDC claims. Matches by provider subject, then by a verified
    /// <c>email</c> claim; a verified match on an unclaimed placeholder is left pending for the person to
    /// accept or decline. Throws when the principal is unusable; callers treat that as Refused(SYNC_FAILED).
    /// </summary>
    Task<UserSyncOutcome> SyncFromClaimsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);

    /// <summary>
    /// The person's answer to a pending placeholder link. Accept claims the placeholder (same
    /// user id); decline releases its email and creates the person's own account, in one save.
    /// </summary>
    Task<LinkResolveOutcome> ResolvePlaceholderLinkAsync(ClaimsPrincipal principal, bool accept, CancellationToken cancellationToken = default);
}

/// <summary>What sync decided about a sign-in.</summary>
public abstract record UserSyncOutcome
{
    private UserSyncOutcome() { }

    public sealed record Synced(User User) : UserSyncOutcome;

    /// <summary>A verified email matches an unclaimed placeholder; the person has to accept or decline.</summary>
    public sealed record PlaceholderLinkPending(Guid PlaceholderUserId, string PlaceholderName) : UserSyncOutcome;

    /// <summary>No session may be issued; <see cref="Code"/> is an <see cref="Error.Codes"/> value.</summary>
    public sealed record Refused(string Code) : UserSyncOutcome;
}

public enum LinkResolveOutcome
{
    Claimed,
    Declined,

    /// <summary>The placeholder was merged, claimed or edited meanwhile (a lost race); sign in again.</summary>
    ChangedMeanwhile,

    /// <summary>The principal has no verified pending link.</summary>
    NotPending
}

/// <summary>Host settings for sync. Placeholders always need a verified email, whatever this says.</summary>
public sealed class UserSyncSettings
{
    /// <summary><c>Authentication:RequireVerifiedEmailForLinking</c>; off only relaxes ordinary accounts.</summary>
    public bool RequireVerifiedEmailForLinking { get; init; } = true;
}

public sealed class UserSynchronizationService : IUserSynchronizationService
{
    private readonly IUserService _userService;
    private readonly IUserRepository _userRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<UserSynchronizationService> _logger;
    private readonly UserSyncSettings _settings;

    public UserSynchronizationService(
        IUserService userService,
        IUserRepository userRepository,
        ISpaceRepository spaceRepository,
        INotificationRepository notificationRepository,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<UserSynchronizationService> logger,
        UserSyncSettings? settings = null)
    {
        _userService = userService;
        _userRepository = userRepository;
        _spaceRepository = spaceRepository;
        _notificationRepository = notificationRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
        _settings = settings ?? new UserSyncSettings();
    }

    public async Task<UserSyncOutcome> SyncFromClaimsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var (subject, provider) = SubjectOf(principal);

        // Prefer an exact provider+subject match for generic OIDC providers with opaque sub values.
        var existingUser = await _userService.GetByProviderUserIdAsync(provider, subject, cancellationToken);

        if (existingUser is not null)
        {
            existingUser.UpdateLastLogin(_clock.UtcNow);
            await _userService.UpdateAsync(existingUser, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Updated last login for existing user {UserId} via provider subject {ProviderUserId}",
                existingUser.Id,
                subject);
            return new UserSyncOutcome.Synced(existingUser);
        }

        // Only the email claim, never preferred_username; its owner must have verified it.
        var email = OidcClaims.EmailOf(principal)
            ?? throw new InvalidOperationException("Email claim not found");
        if (User.IsSentinelEmail(email))
            return new UserSyncOutcome.Refused(Error.Codes.EmailNotVerified);

        var verified = OidcClaims.IsEmailVerified(principal);
        var match = await _userRepository.FindByEmailAsync(email, cancellationToken);

        if (match is { IsPlaceholder: true, IsMerged: false })
        {
            if (!verified)
                return new UserSyncOutcome.Refused(Error.Codes.EmailNotVerified);

            _logger.LogInformation(
                "OIDC subject {ProviderUserId} matches placeholder {PlaceholderId} by verified email; link pending",
                subject,
                match.Id);
            return new UserSyncOutcome.PlaceholderLinkPending(match.Id.Value, match.Name);
        }

        if (match is { IsPlaceholder: false })
        {
            if (!verified && _settings.RequireVerifiedEmailForLinking)
                return new UserSyncOutcome.Refused(Error.Codes.EmailNotVerified);

            var tracked = await _userRepository.GetByIdAsync(match.Id, cancellationToken)
                ?? throw new InvalidOperationException("The matched user disappeared");
            // An email match links only an account that has no provider link yet; it never replaces one.
            if (verified && string.IsNullOrEmpty(tracked.ProviderUserId))
                tracked.LinkProvider(provider, subject);
            tracked.UpdateLastLogin(_clock.UtcNow);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _userService.Evict(tracked.Id, tracked.Email);

            _logger.LogInformation(
                "Matched OIDC user {ProviderUserId} to existing DB user {DbUserId} by email {Email} (verified: {Verified})",
                subject,
                tracked.Id,
                email,
                verified);
            return new UserSyncOutcome.Synced(tracked);
        }

        var newUser = CreateAccount(principal, email, provider, subject);
        await _userService.AddAsync(newUser, cancellationToken);

        _logger.LogInformation(
            "Created new user {UserId} ({Email}) from OIDC provider subject {ProviderUserId}",
            newUser.Id,
            email,
            subject);
        return new UserSyncOutcome.Synced(newUser);
    }

    public async Task<LinkResolveOutcome> ResolvePlaceholderLinkAsync(ClaimsPrincipal principal, bool accept, CancellationToken cancellationToken = default)
    {
        // The pending state is re-derived from the verified email, never from a claim: the web checks its own
        // pending claim before calling, and the API never reads one.
        if (principal.Identity?.IsAuthenticated != true
            || !OidcClaims.IsEmailVerified(principal)
            || OidcClaims.EmailOf(principal) is not { } email
            || User.IsSentinelEmail(email))
        {
            return LinkResolveOutcome.NotPending;
        }

        var (subject, provider) = SubjectOf(principal);

        // A sign-in that already belongs to an account has nothing to link.
        if (await _userService.GetByProviderUserIdAsync(provider, subject, cancellationToken) is not null)
            return LinkResolveOutcome.NotPending;

        // Re-read tracked and re-check: a merge or an edit may have won meanwhile.
        var found = await _userRepository.FindByEmailAsync(email, cancellationToken);
        if (found is null)
            return LinkResolveOutcome.ChangedMeanwhile;

        var placeholder = await _userRepository.GetByIdAsync(found.Id, cancellationToken);
        if (placeholder is not { IsPlaceholder: true, IsMerged: false }
            || !string.Equals(placeholder.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            return LinkResolveOutcome.ChangedMeanwhile;
        }

        var spaces = await _spaceRepository.GetBySpecAsync(new SpacesWithUserMembershipSpecification(placeholder.Id), cancellationToken);
        User? declinedAccount = null;

        if (accept)
        {
            placeholder.ClaimPlaceholder(provider, subject, _clock.UtcNow);
        }
        else
        {
            placeholder.ReleaseEmail();
            declinedAccount = CreateAccount(principal, email, provider, subject);
            await _userRepository.AddAsync(declinedAccount, cancellationToken);
        }

        // Shares the token a concurrent merge bumps, so only one of them commits.
        foreach (var space in spaces)
        {
            space.BumpLedgerVersion();
            await NotifyAdminsAsync(
                space,
                accept
                    ? $"{placeholder.Name} signed in and accepted the placeholder created in {space.Name}."
                    : $"The person invited as placeholder {placeholder.Name} in {space.Name} declined the link; the placeholder stays without an email.",
                placeholder.Id,
                cancellationToken);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogInformation(ex, "Placeholder link for {PlaceholderId} lost a race", placeholder.Id);
            return LinkResolveOutcome.ChangedMeanwhile;
        }

        _userService.Evict(placeholder.Id, email, placeholder.Email);
        _logger.LogInformation(
            "Placeholder {PlaceholderId} {Outcome} by OIDC subject {ProviderUserId}{Account}",
            placeholder.Id,
            accept ? "claimed" : "declined",
            subject,
            declinedAccount is null ? "" : $"; new account {declinedAccount.Id}");

        return accept ? LinkResolveOutcome.Claimed : LinkResolveOutcome.Declined;
    }

    private async Task NotifyAdminsAsync(Domain.Aggregates.Space.Space space, string message, UserId about, CancellationToken cancellationToken)
    {
        foreach (var admin in space.Members.Where(m => m.Role == SpaceRole.Admin && m.UserId != about))
        {
            await _notificationRepository.AddAsync(
                Notification.Create(admin.UserId, NotificationType.SystemAnnouncement, "Placeholder member", message, _clock, space.Id),
                cancellationToken);
        }
    }

    private User CreateAccount(ClaimsPrincipal principal, string email, AuthenticationProvider provider, string subject)
    {
        var name = principal.FindFirst("name")?.Value
            ?? principal.FindFirst("given_name")?.Value
            ?? email.Split('@')[0];
        var pictureUrl = principal.FindFirst("picture")?.Value;

        return User.CreateWithProvider(email, name, provider, subject, _clock.UtcNow, pictureUrl);
    }

    private static (string Subject, AuthenticationProvider Provider) SubjectOf(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            throw new InvalidOperationException("User is not authenticated");

        // The provider's subject; in the API the claims transformation has replaced "sub" and kept it aside.
        var subject = OidcClaims.ProviderSubjectOf(principal);

        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException($"Invalid or missing sub claim: {subject}");

        var identityProvider = principal.FindFirst("identity_provider")?.Value ?? "oidc";
        var provider = identityProvider.ToLowerInvariant() switch
        {
            "google" => AuthenticationProvider.Google,
            "facebook" => AuthenticationProvider.Facebook,
            _ => AuthenticationProvider.Oidc
        };

        return (subject, provider);
    }
}
