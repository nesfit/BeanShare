using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.ActiveQrCodes.Commands;
using BeanShare.Contracts.ActiveQrCodes;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.ActiveQrCodes;

public sealed class RenameActiveQrCodeEndpoint : Endpoint<RenameActiveQrCodeRequest, ActiveQrCodeResponse>
{
    private readonly IMediator _mediator;

    public RenameActiveQrCodeEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/qr-codes/{qrCodeId}/rename");
        Summary(s =>
        {
            s.Summary = "Rename an active QR code";
            s.Description = "Changes the label of an active QR code without altering its product or recipe.";
        });
    }

    public override async Task HandleAsync(RenameActiveQrCodeRequest req, CancellationToken ct)
    {
        var routeQrCodeId = Route<Guid>("qrCodeId");

        var command = new RenameActiveQrCodeCommand(routeQrCodeId, req.Label);

        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            var dto = result.Value;
            var response = new ActiveQrCodeResponse
            {
                Id = dto.Id,
                SpaceId = dto.SpaceId,
                Label = dto.Label,
                ProductName = dto.ProductName,
                ProductBrand = dto.ProductBrand,
                ProductType = dto.ProductType,
                ProductDisplayName = dto.ProductDisplayName,
                RecipeName = dto.RecipeName,
                DefaultGrams = dto.DefaultGrams,
                CreatedAt = dto.CreatedAt,
                IsActive = dto.IsActive,
                StockLotId = dto.StockLotId
            };

            await SendOkAsync(response, ct);
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
