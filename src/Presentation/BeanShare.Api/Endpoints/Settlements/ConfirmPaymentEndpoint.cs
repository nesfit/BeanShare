using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Settlement.Commands.ConfirmPayment;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Settlements;

public sealed class ConfirmPaymentRequest
{
    public Guid SettlementId { get; set; }
    public Guid MemberUserId { get; set; }
}

public sealed class ConfirmPaymentEndpoint : Endpoint<ConfirmPaymentRequest, EmptyResponse>
{
    private readonly IMediator _mediator;

    public ConfirmPaymentEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/settlements/{SettlementId}/confirm-payment/{MemberUserId}");
        Summary(s =>
        {
            s.Summary = "Confirm payment for a settlement line";
            s.Description = "Confirms that a member has paid their settlement amount. Can be called by the member themselves or by an administrator.";
        });
    }

    public override async Task HandleAsync(ConfirmPaymentRequest req, CancellationToken ct)
    {
        var command = new ConfirmPaymentCommand(req.SettlementId, req.MemberUserId);
        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            await SendNoContentAsync(ct);
        }
        else if (!await this.TrySendAccessOrConflictAsync(result.Errors, ct))
        {
            await SendResultAsync(TypedResults.Problem(
                detail: result.Errors.Any() ? result.Errors.First().Message : "Failed to confirm payment",
                statusCode: 400
            ));
        }
    }
}
