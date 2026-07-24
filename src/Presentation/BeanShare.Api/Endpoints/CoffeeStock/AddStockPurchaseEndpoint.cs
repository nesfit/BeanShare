using BeanShare.Api.Endpoints.CoffeeStock.Validators;
using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Contracts.CoffeeStock;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class AddStockPurchaseEndpoint : Endpoint<AddStockPurchaseRequest, StockPurchaseResponse>
{
    private readonly IMediator _mediator;

    public AddStockPurchaseEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{spaceId}/stock/purchases");
        Validator<AddStockPurchaseRequestValidator>();
        Summary(s =>
        {
            s.Summary = "Add coffee stock purchase";
            s.Description = "Records a new coffee purchase for the space. Requires admin privileges. Updates stock levels and creates purchase history. Route parameters must match request body.";
            s.ExampleRequest = new AddStockPurchaseRequest
            {
                BodySpaceId = Guid.NewGuid(),
                ProductName = "Premium Espresso Blend",
                ProductBrand = "Blue Mountain Coffee",
                ProductType = "Espresso",
                QuantityGrams = 1000,
                CostAmount = 25.99m,
                CostCurrency = "USD",
                Vendor = "Local Coffee Roasters",
                PurchasedAt = DateTime.UtcNow.AddDays(-1)
            };
        });
    }

    public override async Task HandleAsync(AddStockPurchaseRequest req, CancellationToken ct)
    {
        var routeSpaceId = Route<Guid>("spaceId");

        // If BodySpaceId is provided and doesn't match route, return error
        if (req.BodySpaceId.HasValue && req.BodySpaceId.Value != routeSpaceId)
        {
            AddError("SpaceId", "Route SpaceId must match request body SpaceId");
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        // Always use route parameter as source of truth
        var command = new AddStockPurchaseCommand(
            routeSpaceId,
            req.ProductName,
            req.ProductBrand,
            req.ProductType,
            req.QuantityGrams,
            req.CostAmount,
            req.CostCurrency,
            req.Vendor,
            req.PurchasedAt,
            req.BagCount);

        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            var dto = result.Value;
            var response = new StockPurchaseResponse
            {
                Id = dto.Id,
                ProductName = dto.ProductName,
                ProductBrand = dto.ProductBrand,
                ProductType = dto.ProductType,
                QuantityGrams = dto.QuantityGrams,
                CostAmount = dto.CostAmount,
                CostCurrency = dto.CostCurrency,
                Vendor = dto.Vendor,
                PurchasedBy = dto.PurchasedBy,
                PurchasedAt = dto.PurchasedAt,
                CreatedAt = dto.CreatedAt,
                CostPerGram = dto.CostPerGram,
                ConsumedGrams = dto.ConsumedGrams,
                RemainingGrams = dto.RemainingGrams,
                Status = dto.Status,
                Label = dto.Label,
                IsOpen = dto.IsOpen,
                OpenedAt = dto.OpenedAt,
                DepletedAt = dto.DepletedAt,
                PurchasedByName = dto.PurchasedByName,
                Message = req.BagCount > 1
                    ? $"Successfully added {req.BagCount} bags of {dto.ProductBrand} {dto.ProductName}"
                    : $"Successfully added {dto.QuantityGrams}g of {dto.ProductBrand} {dto.ProductName}"
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