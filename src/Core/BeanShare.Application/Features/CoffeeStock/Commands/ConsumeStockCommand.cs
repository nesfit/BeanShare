using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.CoffeeStock.Commands;
[RequireSpaceAdmin("SpaceId")]
public sealed record ConsumeStockCommand(
    Guid SpaceId,
    string ProductName,
    string ProductBrand,
    string ProductType,
    decimal QuantityGrams
) : IAuthorize, ICommand<Result<ConsumedStockDto>>;

public sealed record ConsumedStockDto(
    Guid SpaceId,
    string ProductName,
    string ProductBrand,
    string ProductType,
    decimal ConsumedGrams,
    decimal RemainingGrams);