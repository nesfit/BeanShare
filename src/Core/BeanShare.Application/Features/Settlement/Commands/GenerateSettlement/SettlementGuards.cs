using BeanShare.Application.Common;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Enums;

namespace BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;

/// <summary>The ordering and treasurer guards a settlement of a period must pass (design §3.3, G1–G4).</summary>
internal static class SettlementGuards
{
    public static async Task<Error?> CheckAsync(
        Space space,
        BillingPeriod period,
        IReadOnlyList<BillingPeriod> spacePeriods,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        // G1
        if (period.State != BillingState.Closed)
            return Error.InvalidBillingPeriodState("generate settlement", period.State.ToString());

        // G2: every earlier period must be settled (or deleted) first.
        var unsettledEarlier = spacePeriods
            .Where(q => q.Id != period.Id && q.StartDate < period.StartDate && q.State != BillingState.Settled)
            .OrderBy(q => q.StartDate)
            .FirstOrDefault();
        if (unsettledEarlier is not null)
            return Error.SettlementOrder($"Settle or delete '{unsettledEarlier.Name}' first");

        // G3: settlements go strictly in date order.
        var settledLater = spacePeriods
            .Where(q => q.Id != period.Id && q.StartDate > period.StartDate && q.State == BillingState.Settled)
            .OrderBy(q => q.StartDate)
            .FirstOrDefault();
        if (settledLater is not null)
            return Error.SettlementOrder($"'{settledLater.Name}' is already settled; settlements go in date order");

        // G4: an active admin of the space must be the treasurer.
        if (space.TreasurerUserId is not { } treasurerId || !space.IsAdmin(treasurerId))
            return Error.TreasurerRequired();

        var treasurer = await userService.GetByIdForUpdateAsync(treasurerId, cancellationToken);
        if (treasurer is null || !treasurer.IsActive)
            return Error.TreasurerRequired();

        return null;
    }
}
