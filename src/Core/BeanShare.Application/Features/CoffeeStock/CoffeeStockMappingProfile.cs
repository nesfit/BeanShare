using BeanShare.Application.Features.CoffeeStock.Dtos;
using BeanShare.Domain.Aggregates.CoffeeStock;
using Mapster;

namespace BeanShare.Application.Features.CoffeeStock;

public sealed class CoffeeStockMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Purchase, StockPurchaseDto>()
            .Map(dest => dest.Id, src => src.Id)
            .Map(dest => dest.ProductName, src => src.Product.Name)
            .Map(dest => dest.ProductBrand, src => src.Product.Brand)
            .Map(dest => dest.ProductType, src => src.Product.Type.ToString())
            .Map(dest => dest.QuantityGrams, src => src.Quantity.Grams)
            .Map(dest => dest.CostAmount, src => src.Cost.Amount)
            .Map(dest => dest.CostCurrency, src => src.Cost.Currency)
            .Map(dest => dest.Vendor, src => src.Vendor)
            .Map(dest => dest.PurchasedBy, src => src.PurchasedBy.Value)
            .Map(dest => dest.PaidByUserId, src => src.PaidByUserId.HasValue ? src.PaidByUserId.Value.Value : (Guid?)null)
            .Map(dest => dest.PurchasedAt, src => src.PurchasedAt)
            .Map(dest => dest.CreatedAt, src => src.CreatedAt)
            .Map(dest => dest.CostPerGram, src => src.Quantity.Grams > 0 ? src.Cost.Amount / src.Quantity.Grams : 0)
            .Map(dest => dest.ConsumedGrams, src => src.ConsumedGrams)
            .Map(dest => dest.RemainingGrams, src => src.RemainingGrams.Grams)
            .Map(dest => dest.Status, src => src.Status.ToString())
            .Map(dest => dest.Label, src => src.Label)
            .Map(dest => dest.IsOpen, src => src.IsOpen)
            .Map(dest => dest.OpenedAt, src => src.OpenedAt)
            .Map(dest => dest.DepletedAt, src => src.DepletedAt);

        config.NewConfig<StockLevel, StockLevelDto>()
            .Map(dest => dest.Id, src => src.Id)
            .Map(dest => dest.ProductName, src => src.Product.Name)
            .Map(dest => dest.ProductBrand, src => src.Product.Brand)
            .Map(dest => dest.ProductType, src => src.Product.Type.ToString())
            .Map(dest => dest.ProductDisplayName, src => src.Product.DisplayName)
            .Map(dest => dest.TotalPurchasedGrams, src => src.TotalPurchased.Grams)
            .Map(dest => dest.TotalConsumedGrams, src => src.TotalConsumed.Grams)
            .Map(dest => dest.CurrentStockGrams, src => src.CurrentStock.Grams)
            .Map(dest => dest.ConsumptionPercentage, src => src.ConsumptionPercentage)
            .Map(dest => dest.IsArchived, src => src.IsArchived)
            .Map(dest => dest.IsCurrentlyUsed, src => src.IsCurrentlyUsed)
            .Map(dest => dest.UpdatedAt, src => src.UpdatedAt);

        config.NewConfig<Domain.Aggregates.CoffeeStock.CoffeeStock, CoffeeStockDto>()
            .Map(dest => dest.Id, src => src.Id.Value)
            .Map(dest => dest.SpaceId, src => src.SpaceId.Value)
            .Map(dest => dest.CreatedAt, src => src.CreatedAt)
            .Map(dest => dest.UpdatedAt, src => src.UpdatedAt)
            .Map(dest => dest.PurchaseCount, src => src.Purchases.Count)
            .Map(dest => dest.ProductVarietyCount, src => src.ProductVarietyCount)
            .Map(dest => dest.TotalCurrentStockGrams, src => src.TotalCurrentStock.Grams)
            .Map(dest => dest.TotalInvestmentAmount, src => src.Purchases.Sum(p => p.Cost.Amount))
            .Map(dest => dest.TotalInvestmentCurrency, src => src.Purchases.Select(p => p.Cost.Currency).FirstOrDefault() ?? "USD")
            .Map(dest => dest.StockLevels, src => src.StockLevels)
            .Map(dest => dest.RecentPurchases, src => src.Purchases.OrderByDescending(p => p.CreatedAt).Take(10));
    }
}