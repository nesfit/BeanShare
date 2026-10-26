using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Contracts.Spaces;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class JoinSpaceEndpoint : Endpoint<JoinSpaceRequest, JoinSpaceResponse>
{
    private readonly IMediator _mediator;

    public JoinSpaceEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/join");
        AllowAnonymous(); // Intentional - public signup via invite code
        Summary(s =>
        {
            s.Summary = "Join a coffee space via invite code";
            s.Description = "Join an existing coffee space using the invite code";
            s.ExampleRequest = new JoinSpaceRequest { InviteCode = "CAFE23" };
        });
    }

    public override async Task HandleAsync(JoinSpaceRequest req, CancellationToken ct)
    {
        var command = new JoinSpaceCommand(req.InviteCode);
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

        var response = new JoinSpaceResponse
        {
            SpaceId = result.Value.SpaceId,
            SpaceName = result.Value.SpaceName,
            Message = $"Successfully joined coffee space '{result.Value.SpaceName}'"
        };

        await SendOkAsync(response, ct);
    }
}