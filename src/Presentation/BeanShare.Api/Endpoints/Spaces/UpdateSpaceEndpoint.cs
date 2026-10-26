using BeanShare.Api.Endpoints.Common;
using BeanShare.Api.Endpoints.Spaces.Validators;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Contracts.Spaces;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class UpdateSpaceEndpoint : Endpoint<UpdateSpaceRequest, SpaceActionResponse>
{
    private readonly IMediator _mediator;

    public UpdateSpaceEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/spaces/{spaceId}");
        Validator<UpdateSpaceRequestValidator>();
        Summary(s =>
        {
            s.Summary = "Update space name";
            s.Description = "Updates the space name. Requires admin privileges. Route parameters must match request body.";
            s.ExampleRequest = new UpdateSpaceRequest
            {
                SpaceId = Guid.NewGuid(),
                Name = "Updated Coffee Space"
            };
        });
    }

    public override async Task HandleAsync(UpdateSpaceRequest req, CancellationToken ct)
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

        var command = new UpdateSpaceCommand(req.SpaceId, req.Name);
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

        var response = new SpaceActionResponse
        {
            SpaceId = req.SpaceId,
            Name = result.Value.Name,
            IsActive = result.Value.IsActive,
            Message = "Space updated successfully"
        };

        await SendOkAsync(response, ct);
    }
}