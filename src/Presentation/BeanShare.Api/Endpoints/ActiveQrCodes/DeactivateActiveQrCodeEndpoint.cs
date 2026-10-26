using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.ActiveQrCodes.Commands;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.ActiveQrCodes;

public sealed class DeactivateActiveQrCodeEndpoint : EndpointWithoutRequest
{
    private readonly IMediator _mediator;

    public DeactivateActiveQrCodeEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Delete("/api/qr-codes/{qrCodeId}");
        Summary(s =>
        {
            s.Summary = "Deactivate an active QR code";
            s.Description = "Deactivates an active QR code so it can no longer be used for consumption.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var routeQrCodeId = Route<Guid>("qrCodeId");

        var command = new DeactivateActiveQrCodeCommand(routeQrCodeId);
        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            await SendNoContentAsync(ct);
        }
        else
        {
            if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
                return;
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
        }
    }
}
