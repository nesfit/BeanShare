using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Contracts.Spaces;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class CreateSpaceEndpoint : Endpoint<CreateSpaceRequest, CreateSpaceResponse>
{
    private readonly IMediator _mediator;

    public CreateSpaceEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces");
        AllowAnonymous(); // Intentional - public signup flow
        Summary(s =>
        {
            s.Summary = "Create a new coffee space";
            s.Description = "Creates a new coffee space with an auto-generated invite code";
            s.ExampleRequest = new CreateSpaceRequest { Name = "Engineering Team" };
        });
    }

    public override async Task HandleAsync(CreateSpaceRequest req, CancellationToken ct)
    {
        var command = new CreateSpaceCommand(req.Name, req.CurrencyCode);
        var result = await _mediator.Send(command, ct);

        if (result.IsFailure)
        {
            if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
                return;
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync();
            return;
        }

        var response = new CreateSpaceResponse
        {
            SpaceId = result.Value.SpaceId,
            InviteCode = result.Value.InviteCode,
            Message = $"Coffee space '{req.Name}' created successfully"
        };

        await SendCreatedAtAsync("GetSpace", new { id = result.Value.SpaceId }, response, cancellation: ct);
    }
}