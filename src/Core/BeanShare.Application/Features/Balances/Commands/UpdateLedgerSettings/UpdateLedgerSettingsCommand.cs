using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Balances.Dtos;

namespace BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;

/// <summary>
/// Changes the treasurer (an admin of the space) and/or the ledger start date (≤ today; locked after the
/// first ledger settlement). A null value leaves that setting as it is; the Clear flags remove it.
/// </summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record UpdateLedgerSettingsCommand(
    Guid SpaceId,
    Guid? TreasurerUserId,
    DateTime? LedgerStartDate,
    bool ClearTreasurer = false,
    bool ClearLedgerStartDate = false
) : IAuthorize, ICommand<Result<LedgerSettingsDto>>;
