using BeanShare.Application.Features.CoffeeStock.Commands;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class OpenBagEndpoint : EndpointWithoutRequest
{
    private readonly IMediator _mediator;

    public OpenBagEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/stock/bags/{lotId}/open");
        Summary(s =>
        {
            s.Summary = "Open a bag";
            s.Description = "Marks a specific bag (lot) as the one currently being poured from. Requires admin privileges.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var spaceId = Route<Guid>("spaceId");
        var lotId = Route<Guid>("lotId");

        var command = new OpenBagCommand(spaceId, lotId);
        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            await SendOkAsync(new { Message = "Bag opened" }, ct);
        }
        else
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
        }
    }
}
