using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Consumption.Commands.VoidPhantomRows;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Consumption;

public sealed class VoidPhantomRowsRequest
{
    public Guid SpaceId { get; set; }
    public List<Guid> ConsumptionIds { get; set; } = [];
    public string Reason { get; set; } = "";
}

public sealed class VoidPhantomRowsEndpoint : Endpoint<VoidPhantomRowsRequest, EmptyResponse>
{
    private readonly IMediator _mediator;

    public VoidPhantomRowsEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{SpaceId}/consumptions/void-phantom");
        Summary(s =>
        {
            s.Summary = "Void phantom write-off rows";
            s.Description = "Removes write-off shares and bag-less rows without returning their grams to stock. Admin only; all or nothing; cannot be undone.";
        });
    }

    public override async Task HandleAsync(VoidPhantomRowsRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new VoidPhantomRowsCommand(req.SpaceId, req.ConsumptionIds, req.Reason), ct);

        if (result.IsFailure)
        {
            if (await this.TrySendConflictAsync(result.Errors, ct))
                return;
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }
}
