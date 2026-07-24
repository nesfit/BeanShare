using BeanShare.Api.Endpoints.CoffeeStock.Validators;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Contracts.CoffeeStock;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class AddCoffeeStockPurchaseEndpoint : Endpoint<AddStockPurchaseRequest, StockPurchaseResponse>
{
    private readonly IMediator _mediator;

    public AddCoffeeStockPurchaseEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/coffeestock/purchases");
        Validator<AddStockPurchaseRequestValidator>();
        Summary(s =>
        {
            s.Summary = "Add coffee stock purchase";
            s.Description = "Records a new coffee purchase for the space using the new API pattern. Requires admin privileges. Updates stock levels and creates purchase history.";
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
        if (!req.BodySpaceId.HasValue)
        {
            AddError("SpaceId", "SpaceId is required in the request body");
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var command = new AddStockPurchaseCommand(
            req.BodySpaceId.Value,
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

        if (result.IsFailure)
        {
            if (result.Errors.Any(e => e.Code == "stock.currency_conflict"))
            {
                await SendAsync(new StockPurchaseResponse
                {
                    Id = Guid.Empty,
                    ProductName = req.ProductName,
                    ProductBrand = req.ProductBrand,
                    ProductType = req.ProductType,
                    QuantityGrams = req.QuantityGrams,
                    CostAmount = req.CostAmount,
                    CostCurrency = req.CostCurrency,
                    Vendor = req.Vendor,
                    PurchasedBy = Guid.Empty,
                    PurchasedAt = req.PurchasedAt,
                    CreatedAt = DateTime.UtcNow,
                    CostPerGram = 0,
                    Message = result.Errors.First(e => e.Code == "stock.currency_conflict").Message
                }, 409, ct);
                return;
            }

            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
            return;
        }

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

        await SendCreatedAtAsync<GetCoffeeStockEndpoint>(new { spaceId = req.BodySpaceId }, response, cancellation: ct);
    }
}