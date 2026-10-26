using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Billing.Commands.DeleteBillingPeriod;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Billing;

public sealed class DeleteBillingPeriodRequest
{
    public Guid BillingPeriodId { get; set; }
}

public sealed class DeleteBillingPeriodEndpoint : Endpoint<DeleteBillingPeriodRequest, EmptyResponse>
{
    private readonly IMediator _mediator;

    public DeleteBillingPeriodEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Delete("/api/billing-periods/{BillingPeriodId}");
        Summary(s =>
        {
            s.Summary = "Delete a draft billing period";
            s.Description = "Only Draft periods can be deleted (otherwise PERIOD_NOT_DELETABLE). Admin only.";
        });
    }

    public override async Task HandleAsync(DeleteBillingPeriodRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteBillingPeriodCommand(req.BillingPeriodId), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }
}
