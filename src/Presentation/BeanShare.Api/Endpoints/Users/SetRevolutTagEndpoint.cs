using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Users.Commands.SetRevolutTag;
using BeanShare.Contracts.Users;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Users;

public sealed class SetRevolutTagEndpoint : Endpoint<SetRevolutTagRequest>
{
    private readonly IMediator _mediator;

    public SetRevolutTagEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/me/revolut-tag");
        Summary(s =>
        {
            s.Summary = "Set Revolut handle";
            s.Description = "Set the current user's Revolut handle for settlement 'Pay me' links. Pass null/empty to clear.";
        });
    }

    public override async Task HandleAsync(SetRevolutTagRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new SetRevolutTagCommand(req.RevolutTag), ct);

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

        await SendNoContentAsync(ct);
    }
}
