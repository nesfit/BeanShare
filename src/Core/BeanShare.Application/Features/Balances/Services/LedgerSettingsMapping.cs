using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.Space;

namespace BeanShare.Application.Features.Balances.Services;

internal static class LedgerSettingsMapping
{
    public static async Task<LedgerSettingsDto> ToDtoAsync(
        Space space,
        bool hasLedgerSettlement,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        string? treasurerName = null;
        if (space.TreasurerUserId is { } treasurerId)
            treasurerName = (await userService.GetByIdAsync(treasurerId, cancellationToken))?.Name ?? "Unknown";

        return new LedgerSettingsDto(
            space.Id.Value,
            space.Currency.Code,
            space.TreasurerUserId?.Value,
            treasurerName,
            space.LedgerStartDate,
            hasLedgerSettlement);
    }
}
