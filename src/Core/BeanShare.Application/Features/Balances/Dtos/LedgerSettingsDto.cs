namespace BeanShare.Application.Features.Balances.Dtos;

public sealed record LedgerSettingsDto(
    Guid SpaceId,
    string Currency,
    Guid? TreasurerUserId,
    string? TreasurerName,
    DateTime? LedgerStartDate,
    bool IsLedgerStartLocked);
