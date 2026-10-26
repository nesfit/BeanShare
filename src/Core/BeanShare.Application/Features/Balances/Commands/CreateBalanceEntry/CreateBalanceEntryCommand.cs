using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Domain.Enums;

namespace BeanShare.Application.Features.Balances.Commands.CreateBalanceEntry;

/// <summary>
/// Records +<paramref name="Amount"/> for the member and −<paramref name="Amount"/> for the counterparty
/// (default: the caller). Positive means the member is owed.
/// </summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record CreateBalanceEntryCommand(
    Guid SpaceId,
    Guid UserId,
    decimal Amount,
    DateTime EffectiveDate,
    string Note,
    BalanceEntryKind Kind = BalanceEntryKind.Opening,
    Guid? CounterpartyUserId = null
) : IAuthorize, ICommand<Result<BalanceEntryDto>>;
