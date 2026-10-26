using BeanShare.Api.Endpoints.Common;
using BeanShare.Api.Endpoints.Spaces.Validators;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Contracts.Spaces;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class RegenerateInviteCodeEndpoint : Endpoint<RegenerateInviteCodeRequest, InviteCodeResponse>
{
    private readonly IMediator _mediator;

    public RegenerateInviteCodeEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/spaces/{spaceId}/invite-code");
        Validator<RegenerateInviteCodeRequestValidator>();
        Summary(s =>
        {
            s.Summary = "Regenerate space invite code";
            s.Description = "Generates a new invite code for the space. Requires admin privileges. Cannot regenerate for deactivated spaces. Route parameters must match request body.";
            s.ExampleRequest = new RegenerateInviteCodeRequest
            {
                SpaceId = Guid.NewGuid()
            };
        });
    }

    public override async Task HandleAsync(RegenerateInviteCodeRequest req, CancellationToken ct)
    {
        var routeSpaceId = Route<Guid>("spaceId");

        if (req.SpaceId != routeSpaceId)
        {
            AddError("RouteParameterMismatch", "Route SpaceId must match request SpaceId");
        }

        if (ValidationFailed)
        {
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var command = new RegenerateInviteCodeCommand(req.SpaceId);
        var result = await _mediator.Send(command, ct);

        if (result.IsFailure)
        {
            if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
                return;
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var response = new InviteCodeResponse
        {
            SpaceId = req.SpaceId,
            InviteCode = result.Value.InviteCode,
            Message = "Invite code regenerated successfully"
        };

        await SendOkAsync(response, ct);
    }
}