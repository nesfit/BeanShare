using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Domain.Enums;

namespace BeanShare.Application.Features.Balances.Commands.UpdateBalanceEntry;

/// <summary>Edits an entry no settlement has included yet (otherwise ENTRY_LOCKED).</summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record UpdateBalanceEntryCommand(
    Guid SpaceId,
    Guid EntryId,
    Guid UserId,
    Guid CounterpartyUserId,
    decimal Amount,
    DateTime EffectiveDate,
    string Note,
    BalanceEntryKind Kind
) : IAuthorize, ICommand<Result<BalanceEntryDto>>;
