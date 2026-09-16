using BeanShare.Application.Features.CoffeeStock.Queries;
using BeanShare.Contracts.CoffeeStock;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class GetCoffeeStockEndpoint : Endpoint<GetSpaceStockRequest, SpaceStockResponse>
{
    private readonly IMediator _mediator;

    public GetCoffeeStockEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/coffeestock/{spaceId}");
        Summary(s =>
        {
            s.Summary = "Get coffee stock for space";
            s.Description = "Retrieves current coffee stock levels, purchase history, and investment summary for the space using the new API pattern. Requires space membership.";
            s.ExampleRequest = new GetSpaceStockRequest
            {
                SpaceId = Guid.NewGuid()
            };
        });
    }

    public override async Task HandleAsync(GetSpaceStockRequest req, CancellationToken ct)
    {
        var routeSpaceId = Route<Guid>("spaceId");

        var query = new GetSpaceStockQuery(routeSpaceId, req.Page, req.PageSize);
        var result = await _mediator.Send(query, ct);

        if (result.IsFailure)
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var dto = result.Value;
        var response = new SpaceStockResponse
        {
            Id = dto.Id,
            SpaceId = dto.SpaceId,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            PurchaseCount = dto.PurchaseCount,
            ProductVarietyCount = dto.ProductVarietyCount,
            TotalCurrentStockGrams = dto.TotalCurrentStockGrams,
            TotalInvestmentAmount = dto.TotalInvestmentAmount,
            TotalInvestmentCurrency = dto.TotalInvestmentCurrency,
            TotalPurchaseCount = dto.TotalPurchaseCount,
            StockLevels = dto.StockLevels.Select(sl => new StockLevelResponse
            {
                Id = sl.Id,
                ProductName = sl.ProductName,
                ProductBrand = sl.ProductBrand,
                ProductType = sl.ProductType,
                ProductDisplayName = sl.ProductDisplayName,
                TotalPurchasedGrams = sl.TotalPurchasedGrams,
                TotalConsumedGrams = sl.TotalConsumedGrams,
                CurrentStockGrams = sl.CurrentStockGrams,
                ConsumptionPercentage = sl.ConsumptionPercentage,
                IsArchived = sl.IsArchived,
                IsCurrentlyUsed = sl.IsCurrentlyUsed,
                UpdatedAt = sl.UpdatedAt
            }).ToList(),
            RecentPurchases = dto.RecentPurchases.Select(rp => new StockPurchaseResponse
            {
                Id = rp.Id,
                ProductName = rp.ProductName,
                ProductBrand = rp.ProductBrand,
                ProductType = rp.ProductType,
                QuantityGrams = rp.QuantityGrams,
                CostAmount = rp.CostAmount,
                CostCurrency = rp.CostCurrency,
                Vendor = rp.Vendor,
                PurchasedBy = rp.PurchasedBy,
                PurchasedAt = rp.PurchasedAt,
                CreatedAt = rp.CreatedAt,
                CostPerGram = rp.CostPerGram,
                ConsumedGrams = rp.ConsumedGrams,
                RemainingGrams = rp.RemainingGrams,
                Status = rp.Status,
                Label = rp.Label,
                IsOpen = rp.IsOpen,
                OpenedAt = rp.OpenedAt,
                DepletedAt = rp.DepletedAt,
                WrittenOffGrams = rp.WrittenOffGrams,
                DiscardedGrams = rp.DiscardedGrams,
                CanUndoEmpty = rp.CanUndoEmpty,
                UndoEmptyUntil = rp.UndoEmptyUntil,
                PurchasedByName = rp.PurchasedByName,
                Message = ""
            }).ToList()
        };

        await SendOkAsync(response, ct);
    }
}