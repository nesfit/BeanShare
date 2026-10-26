using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Billing.Commands.CreateBillingPeriod;
using BeanShare.Application.Features.Billing.Dtos;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Billing;

public sealed class CreateBillingPeriodRequest
{
    public Guid SpaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public sealed class CreateBillingPeriodEndpoint : Endpoint<CreateBillingPeriodRequest, BillingPeriodDto>
{
    private readonly IMediator _mediator;

    public CreateBillingPeriodEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{SpaceId}/billing-periods");
    }

    public override async Task HandleAsync(CreateBillingPeriodRequest req, CancellationToken ct)
    {
        var command = new CreateBillingPeriodCommand(
            req.SpaceId,
            req.Name,
            DateTime.SpecifyKind(req.StartDate, DateTimeKind.Utc),
            DateTime.SpecifyKind(req.EndDate, DateTimeKind.Utc)
        );

        var result = await _mediator.Send(command, ct);


        if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
            return;

        if (result.IsSuccess)
        {
            await SendCreatedAtAsync<GetBillingPeriodEndpoint>(
                new { BillingPeriodId = result.Value.Id },
                result.Value,
                cancellation: ct);
        }
        else
        {
            await SendResultAsync(TypedResults.Problem(
                detail: result.Errors.Any() ? result.Errors.First().Message : "Failed to create billing period",
                statusCode: 400
            ));
        }
    }
}