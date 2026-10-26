using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class ClearCurrentlyUsedStockLevelEndpoint : EndpointWithoutRequest
{
    private readonly IMediator _mediator;

    public ClearCurrentlyUsedStockLevelEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Delete("/api/spaces/{spaceId}/stock/currently-used");
        Summary(s =>
        {
            s.Summary = "Clear currently used coffee";
            s.Description = "Clears the currently used coffee selection for the space. Requires admin privileges.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var spaceId = Route<Guid>("spaceId");

        var command = new SetCurrentlyUsedStockLevelCommand(spaceId, null);
        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            await SendOkAsync(new { Message = "Currently used coffee cleared" }, ct);
        }
        else
        {
            if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
                return;
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
        }
    }
}
