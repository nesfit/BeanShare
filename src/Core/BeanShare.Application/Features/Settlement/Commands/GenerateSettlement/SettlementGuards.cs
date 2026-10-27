using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Enums;

namespace BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;

/// <summary>The ordering and treasurer guards a settlement of a period must pass. Historical periods never take part in the ordering.</summary>
internal static class SettlementGuards
{
    public static async Task<Error?> CheckAsync(
        Space space,
        BillingPeriod period,
        IReadOnlyList<BillingPeriod> spacePeriods,
        IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        if (period.State != BillingState.Closed)
            return Error.InvalidBillingPeriodState("generate settlement", period.State.ToString());

        // Every earlier period must be settled (or deleted) first.
        var unsettledEarlier = spacePeriods
            .Where(q => q.Id != period.Id && !q.IsHistorical && q.StartDate < period.StartDate && q.State != BillingState.Settled)
            .OrderBy(q => q.StartDate)
            .FirstOrDefault();
        if (unsettledEarlier is not null)
            return Error.SettlementOrder($"Settle or delete '{unsettledEarlier.Name}' first");

        // Settlements go strictly in date order.
        var settledLater = spacePeriods
            .Where(q => q.Id != period.Id && !q.IsHistorical && q.StartDate > period.StartDate && q.State == BillingState.Settled)
            .OrderBy(q => q.StartDate)
            .FirstOrDefault();
        if (settledLater is not null)
            return Error.SettlementOrder($"'{settledLater.Name}' is already settled; settlements go in date order");

        // An active, non-placeholder admin of the space must be the treasurer (read uncached).
        if (space.TreasurerUserId is not { } treasurerId || !space.IsAdmin(treasurerId))
            return Error.TreasurerRequired();

        if ((await userRepository.GetAccessStatusAsync(treasurerId, cancellationToken))?.CanAct != true)
            return Error.TreasurerRequired();

        return null;
    }
}
