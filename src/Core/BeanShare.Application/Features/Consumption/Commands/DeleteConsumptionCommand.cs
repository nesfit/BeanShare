using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.Consumption.Commands;

[RequireSpaceMember("SpaceId")]
public sealed record DeleteConsumptionCommand(
    Guid Id,
    Guid SpaceId
) : IAuthorize, IRetryOnConcurrencyConflict, ICommand<Result<bool>>;
