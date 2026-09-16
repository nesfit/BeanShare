using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Contracts.CoffeeStock;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class DiscardBagEndpoint : EndpointWithoutRequest<DiscardBagResponse>
{
    private readonly IMediator _mediator;

    public DiscardBagEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/stock/bags/{lotId}/discard");
        Summary(s =>
        {
            s.Summary = "Discard a bag (throw the remainder away, bill nobody)";
            s.Description = "Marks a bag as empty without billing its remainder to anyone — for stale or spilled coffee, " +
                            "or a stock record that never matched reality. The discarded grams appear in the ledger as an " +
                            "unattributed outflow. Undoable for 24 h via .../undo-empty. Requires admin privileges.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var spaceId = Route<Guid>("spaceId");
        var lotId = Route<Guid>("lotId");

        var result = await _mediator.Send(new DiscardBagCommand(spaceId, lotId), ct);

        if (result.IsSuccess)
        {
            var r = result.Value;
            await SendOkAsync(new DiscardBagResponse
            {
                LotId = r.LotId,
                ProductDisplayName = r.ProductDisplayName,
                BagLabel = r.BagLabel,
                DiscardedGrams = r.DiscardedGrams,
                SunkCostAmount = r.SunkCostAmount,
                Currency = r.Currency,
                Message = $"Discarded {r.DiscardedGrams:0.#} g from bag {r.BagLabel} of {r.ProductDisplayName} (worth {r.SunkCostAmount:0.##} {r.Currency}); nobody was billed"
            }, ct);
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
