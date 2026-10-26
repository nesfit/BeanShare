using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Features.Settlement.Queries.GetSettlementPreview;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Settlement;

public sealed class GetSettlementPreviewRequest
{
    public Guid BillingPeriodId { get; init; }
}

public sealed class GetSettlementPreviewEndpoint : Endpoint<GetSettlementPreviewRequest, SettlementPreviewDto>
{
    private readonly IMediator _mediator;

    public GetSettlementPreviewEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/billing-periods/{BillingPeriodId}/settlement/preview");
        Summary(s =>
        {
            s.Summary = "Preview the settlement of a closed billing period";
            s.Description = "What generating the settlement would produce right now: lines, treasurer and transfers. Nothing is saved. Admin only.";
        });
    }

    public override async Task HandleAsync(GetSettlementPreviewRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSettlementPreviewQuery(req.BillingPeriodId), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
