namespace BeanShare.Application.Features.Balances.Dtos;

/// <summary>
/// The Balances page: ledger settings, the entries the caller may see (admins: all; members: their own)
/// and how many legacy settlement lines are still unconfirmed.
/// </summary>
public sealed record SpaceBalancesDto(
    Guid SpaceId,
    string Currency,
    bool IsAdmin,
    LedgerSettingsDto Settings,
    IReadOnlyList<BalanceEntryDto> Entries,
    int OpenLegacyLineCount);
