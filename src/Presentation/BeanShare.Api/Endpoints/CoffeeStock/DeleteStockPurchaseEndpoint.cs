using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class DeleteStockPurchaseEndpoint : EndpointWithoutRequest
{
    private readonly IMediator _mediator;

    public DeleteStockPurchaseEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Delete("/api/spaces/{SpaceId}/stock/purchases/{PurchaseId}");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var spaceId = Route<Guid>("SpaceId");
        var purchaseId = Route<Guid>("PurchaseId");

        var command = new DeleteStockPurchaseCommand(spaceId, purchaseId);
        var result = await _mediator.Send(command, ct);

        if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
            return;

        if (result.IsSuccess)
        {
            await SendNoContentAsync(ct);
        }
        else
        {
            await SendResultAsync(Results.BadRequest(result.Errors.First().Message));
        }
    }
}
