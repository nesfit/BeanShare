using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Consumption.Dtos;

namespace BeanShare.Application.Features.Consumption.Commands;

[RequireSpaceAdmin("SpaceId")]
public sealed record ImportConsumptionCsvCommand(
    Guid SpaceId,
    IReadOnlyList<ImportConsumptionCsvRow> Rows
) : IAuthorize, IRetryOnConcurrencyConflict, ICommand<Result<ImportConsumptionCsvResult>>;
