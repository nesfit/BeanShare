using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.CoffeeStock.Dtos;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

public sealed record UpdateStockPurchaseCommand(
    Guid SpaceId,
    Guid PurchaseId,
    decimal QuantityGrams,
    decimal CostAmount,
    DateTime PurchasedAt,
    string? ProductName = null,
    string? ProductBrand = null,
    string? CoffeeType = null,
    Guid? PaidByUserId = null
) : ICommand<Result<StockPurchaseDto>>;
