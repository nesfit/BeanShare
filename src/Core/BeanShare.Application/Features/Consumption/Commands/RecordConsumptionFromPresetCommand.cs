using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Consumption.Dtos;

namespace BeanShare.Application.Features.Consumption.Commands;
[RequireSpaceMember("SpaceId")]
public sealed record RecordConsumptionFromPresetCommand(
    Guid SpaceId,
    Guid PresetId,
    decimal? CustomQuantityGrams = null,
    DateTime? ConsumedAt = null,
    Guid? ForUserId = null
) : IAuthorize, IRetryOnLedgerTokenConflict, ICommand<Result<ConsumptionEntryDto>>;