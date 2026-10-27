using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Infrastructure.Services;
using FastEndpoints;

namespace BeanShare.Api.Endpoints.Me;

public sealed class UploadAvatarEndpoint : EndpointWithoutRequest<UploadAvatarResponse>
{
    private readonly IUserContext _userContext;
    private readonly AvatarStorageService _avatarStorageService;
    private readonly IUserRepository _userRepository;

    public UploadAvatarEndpoint(IUserContext userContext, AvatarStorageService avatarStorageService, IUserRepository userRepository)
    {
        _userContext = userContext;
        _avatarStorageService = avatarStorageService;
        _userRepository = userRepository;
    }

    public override void Configure()
    {
        Post("/api/me/avatar");
        AllowFileUploads();
        Summary(s =>
        {
            s.Summary = "Upload user avatar";
            s.Description = "Upload a profile picture for the current user. Accepts jpg, png, gif, webp formats. Max 5MB.";
            s.Response<UploadAvatarResponse>(200, "Avatar uploaded successfully");
            s.Response(400, "Invalid file format or size");
            s.Response(401, "The account is unknown, deactivated or a placeholder");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // The upload runs no command, so the pipeline's active-account check does not cover it. A principal
        // without a usable id claim is refused the same way, as the pipeline does for a request that needs a user.
        UserId caller;
        try
        {
            caller = _userContext.CurrentUserId;
        }
        catch (InvalidOperationException)
        {
            await this.TrySendAccessOrConflictAsync([Error.Unauthenticated()], ct);
            return;
        }

        var access = await _userRepository.GetAccessStatusAsync(caller, ct);
        if (!access.CanAct)
        {
            await this.TrySendAccessOrConflictAsync([Error.Unauthenticated()], ct);
            return;
        }

        var file = Files.FirstOrDefault();
        if (file is null)
        {
            await SendAsync(new UploadAvatarResponse(false, null, "No file provided"), 400, ct);
            return;
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            await SendAsync(new UploadAvatarResponse(false, null, "File size exceeds 5MB limit"), 400, ct);
            return;
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _avatarStorageService.SaveAvatarAsync(
                caller.Value,
                file.FileName,
                file.ContentType,
                file.Length,
                stream,
                ct);

            if (result.Success)
            {
                await SendOkAsync(new UploadAvatarResponse(true, result.AvatarUrl, null), ct);
                return;
            }

            await SendAsync(new UploadAvatarResponse(false, null, result.Error), 400, ct);
        }
        catch (Exception)
        {
            await SendAsync(new UploadAvatarResponse(false, null, "Failed to upload avatar. Please try again."), 500, ct);
        }
    }
}

public sealed record UploadAvatarResponse(bool Success, string? AvatarUrl, string? Error);
