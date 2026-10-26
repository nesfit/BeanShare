using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Users.Commands.UpdateProfile;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Me;

public sealed class UpdateProfileEndpoint : Endpoint<UpdateProfileRequest, UpdateProfileResponse>
{
    private readonly IMediator _mediator;

    public UpdateProfileEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/me/profile");
        Summary(s =>
        {
            s.Summary = "Update user profile";
            s.Description = "Update the current user's profile information including name and picture URL.";
            s.Response<UpdateProfileResponse>(200, "Profile updated successfully");
            s.Response(400, "Invalid request");
            s.Response(404, "User not found");
        });
    }

    public override async Task HandleAsync(UpdateProfileRequest req, CancellationToken ct)
    {
        var command = new UpdateProfileCommand(req.Name, req.PictureUrl);
        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            await SendOkAsync(new UpdateProfileResponse(true, null), ct);
        }
        else
        {
            if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
                return;
            var errorMessage = result.Errors.Any() ? result.Errors.First().Message : "Failed to update profile";
            await SendAsync(new UpdateProfileResponse(false, errorMessage), 400, ct);
        }
    }
}

public sealed record UpdateProfileRequest(string Name, string? PictureUrl);
public sealed record UpdateProfileResponse(bool Success, string? Error);
