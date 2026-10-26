using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Contracts.Spaces;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class LeaveSpaceEndpoint : Endpoint<LeaveSpaceRequest, LeaveSpaceResponse>
{
    private readonly IMediator _mediator;

    public LeaveSpaceEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/leave");
        Summary(s =>
        {
            s.Summary = "Leave a coffee space";
            s.Description = "Allows a non-admin member to leave a coffee space. Admins cannot leave; they must be demoted first or transfer ownership.";
            s.ExampleRequest = new LeaveSpaceRequest
            {
                SpaceId = Guid.NewGuid()
            };
        });
    }

    public override async Task HandleAsync(LeaveSpaceRequest req, CancellationToken ct)
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

        var command = new LeaveSpaceCommand(req.SpaceId);
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

        var response = new LeaveSpaceResponse
        {
            SpaceId = result.Value.SpaceId,
            SpaceName = result.Value.SpaceName,
            Message = $"Successfully left coffee space '{result.Value.SpaceName}'"
        };

        await SendOkAsync(response, ct);
    }
}
