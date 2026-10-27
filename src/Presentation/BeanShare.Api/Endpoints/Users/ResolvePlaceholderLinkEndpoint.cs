using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Common;
using BeanShare.Application.Services;
using FastEndpoints;

namespace BeanShare.Api.Endpoints.Users;

public sealed class ResolvePlaceholderLinkRequest
{
    public bool Accept { get; set; }
}

public sealed class ResolvePlaceholderLinkResponse
{
    /// <summary>"Claimed" or "Declined".</summary>
    public string Outcome { get; init; } = "";
}

/// <summary>
/// An API client's answer to PLACEHOLDER_LINK_PENDING: accept claims the placeholder (same user id),
/// decline creates the person's own account. A lost race against a merge is 409 CONCURRENCY_CONFLICT.
/// </summary>
public sealed class ResolvePlaceholderLinkEndpoint : Endpoint<ResolvePlaceholderLinkRequest, ResolvePlaceholderLinkResponse>
{
    private readonly IUserSynchronizationService _userSyncService;
    private readonly ILogger<ResolvePlaceholderLinkEndpoint> _logger;

    public ResolvePlaceholderLinkEndpoint(IUserSynchronizationService userSyncService, ILogger<ResolvePlaceholderLinkEndpoint> logger)
    {
        _userSyncService = userSyncService;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/api/users/sync/placeholder-link");
        Summary(s =>
        {
            s.Summary = "Accept or decline a pending placeholder link";
            s.Description = "Resolves the PLACEHOLDER_LINK_PENDING answer of /api/users/sync for a verified email.";
        });
    }

    public override async Task HandleAsync(ResolvePlaceholderLinkRequest req, CancellationToken ct)
    {
        LinkResolveOutcome outcome;
        try
        {
            outcome = await _userSyncService.ResolvePlaceholderLinkAsync(HttpContext.User, req.Accept, ct);
        }
        catch (Exception ex)
        {
            // Like sync: a failure refuses the answer instead of a 500.
            _logger.LogError(ex, "Placeholder link failed");
            await this.SendResultErrorsAsync(Result.Failure(Error.SyncFailed()), ct);
            return;
        }

        switch (outcome)
        {
            case LinkResolveOutcome.Claimed:
            case LinkResolveOutcome.Declined:
                await SendOkAsync(new ResolvePlaceholderLinkResponse { Outcome = outcome.ToString() }, ct);
                break;
            case LinkResolveOutcome.ChangedMeanwhile:
                await this.SendResultErrorsAsync(Result.Failure(Error.ConcurrencyConflict()), ct);
                break;
            default:
                await this.SendResultErrorsAsync(Result.Failure(Error.NotFound("Placeholder link", "No placeholder is waiting for this sign-in")), ct);
                break;
        }
    }
}
