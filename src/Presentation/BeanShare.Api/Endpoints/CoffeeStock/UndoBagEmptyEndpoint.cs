using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Contracts.CoffeeStock;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class UndoBagEmptyEndpoint : EndpointWithoutRequest<UndoBagEmptyResponse>
{
    private readonly IMediator _mediator;

    public UndoBagEmptyEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/stock/bags/{lotId}/undo-empty");
        Summary(s =>
        {
            s.Summary = "Undo emptying a bag";
            s.Description = "Reverses an accidental 'empty bag': deletes the cups it billed to the bag's drinkers and puts " +
                            "the written-off grams back into the bag. Allowed within 24 h of the emptying while none of those " +
                            "cups is in a closed/settled billing period. Requires admin privileges.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var spaceId = Route<Guid>("spaceId");
        var lotId = Route<Guid>("lotId");

        var result = await _mediator.Send(new UndoBagEmptyCommand(spaceId, lotId), ct);


        if (await this.TrySendConflictAsync(result.Errors, ct))

            return;

        if (result.IsSuccess)
        {
            var r = result.Value;
            await SendOkAsync(new UndoBagEmptyResponse
            {
                LotId = r.LotId,
                ProductDisplayName = r.ProductDisplayName,
                BagLabel = r.BagLabel,
                RestoredGrams = r.RestoredGrams,
                RemovedEntries = r.RemovedEntries,
                NewStatus = r.NewStatus,
                Message = $"Restored {r.RestoredGrams:0.#} g to bag {r.BagLabel} of {r.ProductDisplayName}; removed {r.RemovedEntries} billed cup(s)"
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
