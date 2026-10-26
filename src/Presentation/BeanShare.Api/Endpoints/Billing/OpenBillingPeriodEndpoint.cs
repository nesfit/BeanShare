using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Billing.Commands.OpenBillingPeriod;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BeanShare.Api.Endpoints.Billing;

public sealed class OpenBillingPeriodRequest
{
    public Guid BillingPeriodId { get; set; }
}

public sealed class OpenBillingPeriodEndpoint : Endpoint<OpenBillingPeriodRequest, EmptyResponse>
{
    private readonly IMediator _mediator;

    public OpenBillingPeriodEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/billing-periods/{BillingPeriodId}/open");
    }

    public override async Task HandleAsync(OpenBillingPeriodRequest req, CancellationToken ct)
    {
        var command = new OpenBillingPeriodCommand(req.BillingPeriodId);
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
                detail: result.Errors.Any() ? result.Errors.First().Message : "Failed to open billing period",
                statusCode: 400
            ));
        }
    }
}