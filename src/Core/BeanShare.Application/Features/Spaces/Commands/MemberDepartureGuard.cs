using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;

namespace BeanShare.Application.Features.Spaces.Commands;

/// <summary>
/// Who may not leave or be removed: the treasurer, a member with an unconfirmed legacy line,
/// the payer of a bag that still holds coffee, anyone whose running balance is not zero, and a merged
/// placeholder.
/// </summary>
internal static class MemberDepartureGuard
{
    public static async Task<Error?> CheckAsync(
        Space space,
        UserId member,
        ISettlementRepository settlementRepository,
        LedgerSnapshotService ledgerSnapshotService,
        IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        // A merged placeholder stays a hidden member for later sweeps; read uncached.
        if (await userRepository.IsMergedAsync(member, cancellationToken))
            return Error.MemberMerged();

        if (space.IsTreasurer(member))
            return Error.MemberHasBalance("The treasurer cannot leave or be removed; choose another treasurer first");

        var settlements = await settlementRepository.GetBySpaceIdAsync(space.Id, cancellationToken);
        if (settlements.Any(s => !s.IsLedger && s.Lines.Any(l => l.UserId == member && !l.IsConfirmed)))
            return Error.UnpaidSettlements(member.Value);

        var snapshot = await ledgerSnapshotService.RunningAsync(space, cancellationToken);

        if (snapshot.Stock?.Purchases.Any(p => !p.IsEmpty && p.RemainingGrams.IsPositive && p.EffectivePayer == member) == true)
            return Error.MemberHasBalance("This member paid for a bag that still holds coffee; change who paid for it first");

        var balance = snapshot.Calculation.NetFor(member);
        if (balance != 0)
            return Error.MemberHasBalance($"This member's running balance is {space.Currency.FormatAmount(balance)}; settle it first");

        return null;
    }
}
