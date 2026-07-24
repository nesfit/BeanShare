using BeanShare.Application.Features.ActiveQrCodes.Queries;
using BeanShare.Contracts.ActiveQrCodes;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.ActiveQrCodes;

public sealed class GetActiveQrCodesEndpoint : EndpointWithoutRequest<List<ActiveQrCodeResponse>>
{
    private readonly IMediator _mediator;

    public GetActiveQrCodesEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/spaces/{spaceId}/qr-codes");
        Summary(s =>
        {
            s.Summary = "Get active QR codes for a space";
            s.Description = "Returns all active QR codes configured for the specified space.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var routeSpaceId = Route<Guid>("spaceId");

        var query = new GetActiveQrCodesQuery(routeSpaceId);
        var result = await _mediator.Send(query, ct);

        if (result.IsSuccess)
        {
            var response = result.Value.Select(dto => new ActiveQrCodeResponse
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
            }).ToList();

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
