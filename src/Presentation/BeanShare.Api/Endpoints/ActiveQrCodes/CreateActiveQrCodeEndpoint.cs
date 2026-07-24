using BeanShare.Application.Features.ActiveQrCodes.Commands;
using BeanShare.Contracts.ActiveQrCodes;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.ActiveQrCodes;

public sealed class CreateActiveQrCodeEndpoint : Endpoint<CreateActiveQrCodeRequest, ActiveQrCodeResponse>
{
    private readonly IMediator _mediator;

    public CreateActiveQrCodeEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/qr-codes");
        Summary(s =>
        {
            s.Summary = "Create an active QR code";
            s.Description = "Creates a new active QR code for a product in the space.";
        });
    }

    public override async Task HandleAsync(CreateActiveQrCodeRequest req, CancellationToken ct)
    {
        var routeSpaceId = Route<Guid>("spaceId");

        var command = new CreateActiveQrCodeCommand(
            routeSpaceId,
            req.Label,
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
            await SendAsync(response, 201, ct);
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
