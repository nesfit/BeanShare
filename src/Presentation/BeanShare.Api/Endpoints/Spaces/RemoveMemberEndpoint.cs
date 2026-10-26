using BeanShare.Api.Endpoints.Common;
using BeanShare.Api.Endpoints.Spaces.Validators;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Contracts.Spaces;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class RemoveMemberEndpoint : Endpoint<RemoveMemberRequest, MemberActionResponse>
{
    private readonly IMediator _mediator;

    public RemoveMemberEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/members/{userId}/remove");
        Validator<RemoveMemberRequestValidator>();
        Summary(s =>
        {
            s.Summary = "Remove member from space";
            s.Description = "Removes a member from the space. Requires admin privileges. Cannot remove the last admin. Route parameters must match request body.";
            s.ExampleRequest = new RemoveMemberRequest
            {
                SpaceId = Guid.NewGuid(),
                UserId = Guid.NewGuid()
            };
        });
    }

    public override async Task HandleAsync(RemoveMemberRequest req, CancellationToken ct)
    {
        var routeSpaceId = Route<Guid>("spaceId");
        var routeUserId = Route<Guid>("userId");

        if (req.SpaceId != routeSpaceId)
        {
            AddError("RouteParameterMismatch", "Route SpaceId must match request SpaceId");
        }

        if (req.UserId != routeUserId)
        {
            AddError("RouteParameterMismatch", "Route UserId must match request UserId");
        }

        if (ValidationFailed)
        {
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var command = new RemoveMemberCommand(req.SpaceId, req.UserId);
        var result = await _mediator.Send(command, ct);

        if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
            return;

        if (result.IsFailure)
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var response = new MemberActionResponse
        {
            SpaceId = req.SpaceId,
            UserId = req.UserId,
            Message = "Member removed from space successfully"
        };

        await SendOkAsync(response, ct);
    }
}