using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Common;
using BeanShare.Application.Services;
using BeanShare.Contracts.Users;
using FastEndpoints;

namespace BeanShare.Api.Endpoints.Users;

/// <summary>
/// Endpoint to synchronize user from OIDC provider to local database.
/// Called by clients after authenticating with the OIDC provider.
/// </summary>
public sealed class SyncUserEndpoint : EndpointWithoutRequest<SyncUserResponse>
{
    private readonly IUserSynchronizationService _userSyncService;
    private readonly ILogger<SyncUserEndpoint> _logger;

    public SyncUserEndpoint(IUserSynchronizationService userSyncService, ILogger<SyncUserEndpoint> logger)
    {
        _userSyncService = userSyncService;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/api/users/sync");
        Summary(s =>
        {
            s.Summary = "Sync user from OIDC provider";
            s.Description = "Creates or updates the local user record from OIDC claims. " +
                          "Call this after successful OIDC authentication to ensure user exists in BeanShare.";
            s.Response<SyncUserResponse>(200, "User synchronized successfully");
            s.Response(401, "User is not authenticated");
            s.Response(403, "EMAIL_NOT_VERIFIED or SYNC_FAILED: no account may act for this sign-in");
            s.Response(409, "PLACEHOLDER_LINK_PENDING: accept or decline the placeholder link first");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        UserSyncOutcome outcome;
        try
        {
            outcome = await _userSyncService.SyncFromClaimsAsync(HttpContext.User, ct);
        }
        catch (Exception ex)
        {
            // Any failure refuses the sign-in instead of a 500.
            _logger.LogError(ex, "User sync failed");
            outcome = new UserSyncOutcome.Refused(Error.Codes.SyncFailed);
        }

        switch (outcome)
        {
            case UserSyncOutcome.Synced { User: var user }:
                await SendOkAsync(new SyncUserResponse
                {
                    UserId = user.Id.Value,
                    Email = user.Email,
                    Name = user.Name,
                    PictureUrl = user.PictureUrl,
                    Provider = user.Provider.ToString(),
                    IsNewUser = user.CreatedAt == user.LastLoginAt
                }, ct);
                break;

            case UserSyncOutcome.PlaceholderLinkPending:
                await this.SendResultErrorsAsync(Result.Failure(Error.PlaceholderLinkPending()), ct);
                break;

            case UserSyncOutcome.Refused { Code: var code }:
                await this.SendResultErrorsAsync(
                    Result.Failure(code == Error.Codes.EmailNotVerified ? Error.EmailNotVerified() : Error.SyncFailed()), ct);
                break;
        }
    }
}
