using BeanShare.Application.Features.ActiveQrCodes.Queries;
using BeanShare.Contracts.ActiveQrCodes;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.ActiveQrCodes;

public sealed class ResolveActiveQrCodeEndpoint : EndpointWithoutRequest<ActiveQrCodeResponse>
{
    private readonly IMediator _mediator;

    public ResolveActiveQrCodeEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/qr-codes/{qrCodeId}/resolve");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Resolve an active QR code";
            s.Description = "Resolves a QR code ID to its associated product and recipe details.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var routeQrCodeId = Route<Guid>("qrCodeId");

        var query = new ResolveActiveQrCodeQuery(routeQrCodeId);
        var result = await _mediator.Send(query, ct);

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
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
        }
    }
}
