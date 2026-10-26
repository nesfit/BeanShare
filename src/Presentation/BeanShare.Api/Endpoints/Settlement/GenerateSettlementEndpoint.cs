using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;
using BeanShare.Application.Features.Settlement.Dtos;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Settlement;

public sealed class GenerateSettlementRequest
{
    public Guid BillingPeriodId { get; set; }
}

public sealed class GenerateSettlementEndpoint : Endpoint<GenerateSettlementRequest, SettlementDto>
{
    private readonly IMediator _mediator;

    public GenerateSettlementEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/billing-periods/{BillingPeriodId}/settlement");
    }

    public override async Task HandleAsync(GenerateSettlementRequest req, CancellationToken ct)
    {
        var command = new GenerateSettlementCommand(req.BillingPeriodId);
        var result = await _mediator.Send(command, ct);

        if (await this.TrySendConflictAsync(result.Errors, ct))
            return;

        if (result.IsSuccess)
        {
            await SendOkAsync(result.Value, ct);
        }
        else
        {
            await SendResultAsync(TypedResults.Problem(
                detail: result.Errors.Any() ? result.Errors.First().Message : "Failed to generate settlement",
                statusCode: 400
            ));
        }
    }
}