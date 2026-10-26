using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Consumption.Dtos;

namespace BeanShare.Application.Features.Consumption.Commands;

[RequireSpaceMember("SpaceId")]
public sealed record UpdateConsumptionCommand(
    Guid Id,
    Guid SpaceId,
    string ProductName,
    string ProductBrand,
    string ProductType,
    decimal QuantityGrams,
    DateTime ConsumedAt
) : IAuthorize, IRetryOnConcurrencyConflict, ICommand<Result<ConsumptionEntryDto>>;
