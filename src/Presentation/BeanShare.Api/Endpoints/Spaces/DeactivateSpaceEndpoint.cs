using BeanShare.Api.Endpoints.Common;
using BeanShare.Api.Endpoints.Spaces.Validators;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Contracts.Spaces;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class DeactivateSpaceEndpoint : Endpoint<DeactivateSpaceRequest, SpaceActionResponse>
{
    private readonly IMediator _mediator;

    public DeactivateSpaceEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/deactivate");
        Validator<DeactivateSpaceRequestValidator>();
        Summary(s =>
        {
            s.Summary = "Deactivate space";
            s.Description = "Deactivates/archives the space. Requires admin privileges. Route parameters must match request body.";
            s.ExampleRequest = new DeactivateSpaceRequest
            {
                SpaceId = Guid.NewGuid()
            };
        });
    }

    public override async Task HandleAsync(DeactivateSpaceRequest req, CancellationToken ct)
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

        var command = new DeactivateSpaceCommand(req.SpaceId);
        var result = await _mediator.Send(command, ct);

        if (result.IsFailure)
        {
            if (await this.TrySendConflictAsync(result.Errors, ct))
                return;
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var response = new SpaceActionResponse
        {
            SpaceId = req.SpaceId,
            Name = result.Value.Name,
            IsActive = result.Value.IsActive,
            Message = "Space deactivated successfully"
        };

        await SendOkAsync(response, ct);
    }
}