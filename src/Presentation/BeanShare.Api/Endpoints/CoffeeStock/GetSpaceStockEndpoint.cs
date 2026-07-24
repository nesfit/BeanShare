using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.CoffeeStock.Queries;
using BeanShare.Contracts.CoffeeStock;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.CoffeeStock;

public sealed class GetSpaceStockEndpoint : Endpoint<GetSpaceStockRequest, SpaceStockResponse>
{
    private readonly IMediator _mediator;

    public GetSpaceStockEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/spaces/{spaceId}/stock");
        Summary(s =>
        {
            s.Summary = "Get space coffee stock";
            s.Description = "Retrieves current coffee stock levels, purchase history, and investment summary for the space. Requires space membership.";
            s.ExampleRequest = new GetSpaceStockRequest
            {
                SpaceId = Guid.NewGuid()
            };
        });
    }

    public override async Task HandleAsync(GetSpaceStockRequest req, CancellationToken ct)
    {
        var routeSpaceId = Route<Guid>("spaceId");
        req = req with { SpaceId = routeSpaceId };

        var query = new GetSpaceStockQuery(req.SpaceId, req.Page, req.PageSize);
        var result = await _mediator.Send(query, ct);

        if (result.IsSuccess)
        {
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
                    PurchasedByName = rp.PurchasedByName,
                    Message = ""
                }).ToList()
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