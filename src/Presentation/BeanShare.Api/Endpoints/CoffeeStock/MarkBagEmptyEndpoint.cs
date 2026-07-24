using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Contracts.CoffeeStock;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class MarkBagEmptyEndpoint : Endpoint<MarkBagEmptyRequest, MarkBagEmptyResponse>
{
    private readonly IMediator _mediator;

    public MarkBagEmptyEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/stock/bags/{lotId}/empty");
        Summary(s =>
        {
            s.Summary = "Empty a bag (phantom-fill)";
            s.Description = "Marks a bag as empty. The untracked remainder is billed to the bag's drinkers as " +
                            "consumption entries at the bag's own cost-per-gram. Requires admin privileges.";
        });
    }

    public override async Task HandleAsync(MarkBagEmptyRequest req, CancellationToken ct)
    {
        var spaceId = Route<Guid>("spaceId");
        var lotId = Route<Guid>("lotId");

        var command = new MarkBagEmptyCommand(spaceId, lotId, req.Reason);
        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            var r = result.Value;
            var response = new MarkBagEmptyResponse
            {
                LotId = r.LotId,
                ProductDisplayName = r.ProductDisplayName,
                BagLabel = r.BagLabel,
                DistributedGrams = r.DistributedGrams,
                MemberCount = r.MemberCount,
                Message = $"Emptied bag {r.BagLabel} of {r.ProductDisplayName}; billed {r.DistributedGrams:0.#} g to {r.MemberCount} member(s)"
            };
            await SendOkAsync(response, ct);
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
