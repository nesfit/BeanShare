namespace BeanShare.Application.Features.Balances.Dtos;

/// <summary>
/// The Balances page: ledger settings, the running balances and entries the caller may see (admins: all;
/// members: their own) and how many legacy settlement lines are still unconfirmed. Until a treasurer is set,
/// <see cref="UnassignedRounding"/> (admins only) holds the rounding residual no line carries, so the
/// balances plus it sum to zero.
/// </summary>
public sealed record SpaceBalancesDto(
    Guid SpaceId,
    string Currency,
    bool IsAdmin,
    LedgerSettingsDto Settings,
    IReadOnlyList<BalanceEntryDto> Entries,
    int OpenLegacyLineCount,
    IReadOnlyList<MemberBalanceDto> Members,
    decimal UnassignedRounding = 0m);
