using BeanShare.Api.Endpoints.CoffeeStock.Validators;
using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Contracts.CoffeeStock;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class ConsumeStockEndpoint : Endpoint<ConsumeStockRequest, ConsumeStockResponse>
{
    private readonly IMediator _mediator;

    public ConsumeStockEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/stock/consume");
        Validator<ConsumeStockRequestValidator>();
        Summary(s =>
        {
            s.Summary = "Consume coffee stock";
            s.Description = "Records coffee consumption from stock. Requires admin privileges. Updates stock levels and tracks consumption.";
            s.ExampleRequest = new ConsumeStockRequest
            {
                SpaceId = Guid.NewGuid(),
                ProductName = "Premium Espresso Blend",
                ProductBrand = "Blue Mountain Coffee",
                ProductType = "Espresso",
                QuantityGrams = 15
            };
        });
    }

    public override async Task HandleAsync(ConsumeStockRequest req, CancellationToken ct)
    {
        var routeSpaceId = Route<Guid>("spaceId");

        if (req.SpaceId != routeSpaceId)
        {
            AddError("RouteParameterMismatch", "Route SpaceId must match request SpaceId");
        }

        if (ValidationFailed)
        {
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var command = new ConsumeStockCommand(
            req.SpaceId,
            req.ProductName,
            req.ProductBrand,
            req.ProductType,
            req.QuantityGrams);

        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            var consumedStock = result.Value;
            var response = new ConsumeStockResponse
            {
                SpaceId = consumedStock.SpaceId,
                ProductName = consumedStock.ProductName,
                ProductBrand = consumedStock.ProductBrand,
                ProductType = consumedStock.ProductType,
                ConsumedGrams = consumedStock.ConsumedGrams,
                RemainingGrams = consumedStock.RemainingGrams,
                Message = $"Successfully consumed {consumedStock.ConsumedGrams}g of {consumedStock.ProductBrand} {consumedStock.ProductName}"
            };
            await SendOkAsync(response, ct);
        }
        else
        {
            if (await this.TrySendConflictAsync(result.Errors, ct))
                return;
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
        }
    }
}