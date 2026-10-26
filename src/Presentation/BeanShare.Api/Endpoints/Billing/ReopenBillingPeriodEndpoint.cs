using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Billing.Commands.ReopenBillingPeriod;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BeanShare.Api.Endpoints.Billing;

public sealed class ReopenBillingPeriodRequest
{
    public Guid BillingPeriodId { get; set; }
}

public sealed class ReopenBillingPeriodEndpoint : Endpoint<ReopenBillingPeriodRequest, EmptyResponse>
{
    private readonly IMediator _mediator;

    public ReopenBillingPeriodEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/billing-periods/{BillingPeriodId}/reopen");
    }

    public override async Task HandleAsync(ReopenBillingPeriodRequest req, CancellationToken ct)
    {
        var command = new ReopenBillingPeriodCommand(req.BillingPeriodId);
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
                detail: result.Errors.Any() ? result.Errors.First().Message : "Failed to reopen billing period",
                statusCode: 400
            ));
        }
    }
}
