using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Billing.Commands.CloseBillingPeriod;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Billing;

public sealed class CloseBillingPeriodRequest
{
    public Guid BillingPeriodId { get; set; }
}

public sealed class CloseBillingPeriodEndpoint : Endpoint<CloseBillingPeriodRequest, EmptyResponse>
{
    private readonly IMediator _mediator;

    public CloseBillingPeriodEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/billing-periods/{BillingPeriodId}/close");
    }

    public override async Task HandleAsync(CloseBillingPeriodRequest req, CancellationToken ct)
    {
        var command = new CloseBillingPeriodCommand(req.BillingPeriodId);
        var result = await _mediator.Send(command, ct);

        if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
            return;

        if (result.IsSuccess)
        {
            await SendNoContentAsync(ct);
        }
        else
        {
            await SendResultAsync(TypedResults.Problem(
                detail: result.Errors.Any() ? result.Errors.First().Message : "Failed to close billing period",
                statusCode: 400
            ));
        }
    }
}