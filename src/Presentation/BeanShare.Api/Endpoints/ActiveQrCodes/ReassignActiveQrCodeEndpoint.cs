using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.ActiveQrCodes.Commands;
using BeanShare.Contracts.ActiveQrCodes;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.ActiveQrCodes;

public sealed class ReassignActiveQrCodeEndpoint : Endpoint<ReassignActiveQrCodeRequest, ActiveQrCodeResponse>
{
    private readonly IMediator _mediator;

    public ReassignActiveQrCodeEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/qr-codes/{qrCodeId}/reassign");
        Summary(s =>
        {
            s.Summary = "Reassign an active QR code";
            s.Description = "Reassigns an active QR code to a different product and recipe.";
        });
    }

    public override async Task HandleAsync(ReassignActiveQrCodeRequest req, CancellationToken ct)
    {
        var routeQrCodeId = Route<Guid>("qrCodeId");

        var command = new ReassignActiveQrCodeCommand(
            routeQrCodeId,
            req.ProductName,
            req.ProductBrand,
            req.ProductType,
            req.RecipeName,
            req.DefaultGrams,
            req.StockLotId);

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
