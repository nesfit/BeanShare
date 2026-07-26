using BeanShare.Application.Features.CoffeeStock.Queries.LookupBarcode;
using BeanShare.Contracts.CoffeeStock;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class LookupBarcodeEndpoint : EndpointWithoutRequest<BarcodeLookupResponse>
{
    private readonly IMediator _mediator;

    public LookupBarcodeEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/spaces/{spaceId}/stock/barcode/{barcode}");
        Summary(s =>
        {
            s.Summary = "Look up a barcode";
            s.Description = "Resolves a scanned barcode into prefill data for adding a bag: first from this space's own " +
                            "purchase history (remembering the last price paid), then from an external product catalog " +
                            "(name/brand/weight only). Requires space membership.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var spaceId = Route<Guid>("spaceId");
        var barcode = Route<string>("barcode")!;

        var query = new LookupBarcodeQuery(spaceId, barcode);
        var result = await _mediator.Send(query, ct);

        if (result.IsSuccess)
        {
            var dto = result.Value;
            var response = new BarcodeLookupResponse(
                dto.Found,
                dto.ProductName,
                dto.ProductBrand,
                dto.ProductType,
                dto.QuantityGrams,
                dto.CostAmount,
                dto.Source);
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
