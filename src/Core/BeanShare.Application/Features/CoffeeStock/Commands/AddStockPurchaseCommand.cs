using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.CoffeeStock.Dtos;

namespace BeanShare.Application.Features.CoffeeStock.Commands;
[RequireSpaceAdmin("SpaceId")]
public sealed record AddStockPurchaseCommand(
    Guid SpaceId,
    string ProductName,
    string ProductBrand,
    string ProductType,
    decimal QuantityGrams,
    decimal CostAmount,
    string CostCurrency,
    string Vendor,
    DateTime PurchasedAt,
    int BagCount = 1,
    string? Barcode = null
) : IAuthorize, ICommand<Result<StockPurchaseDto>>;