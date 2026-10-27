using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Services;
using SettlementAggregate = BeanShare.Domain.Aggregates.Settlement.Settlement;

namespace BeanShare.Application.Features.Settlement;

/// <summary>Builds the settlement DTO from the stored snapshot only, never from live data.</summary>
internal static class SettlementDtoMapper
{
    public static IEnumerable<UserId> UserIdsOf(SettlementAggregate settlement) =>
        settlement.Lines.Select(l => l.UserId)
            .Concat(settlement.TreasurerUserId is { } treasurer ? [treasurer] : [])
            .Distinct();

    /// <param name="legacyPaid">Live "Paid" for legacy lines the backfill has not frozen yet.</param>
    public static SettlementDto ToDto(
        SettlementAggregate settlement,
        BillingPeriod period,
        IReadOnlyDictionary<UserId, User> users,
        IReadOnlyDictionary<UserId, decimal>? legacyPaid = null)
    {
        var totalGrams = settlement.Lines.Sum(l => l.TotalCoffeeGrams);

        var lines = settlement.Lines
            .Select(line =>
            {
                users.TryGetValue(line.UserId, out var user);
                var amountPaid = settlement.IsLedger
                    ? line.CreditAmount ?? 0m
                    : line.LegacyAmountPaid ?? legacyPaid?.GetValueOrDefault(line.UserId) ?? 0m;
                var net = settlement.IsLedger ? line.NetAmount ?? 0m : amountPaid - line.AmountDue.Amount;

                return new SettlementLineDto(
                    line.UserId.Value,
                    user?.Name ?? $"User {line.UserId.Value}",
                    user?.PublicEmail ?? string.Empty,
                    line.TotalCoffeeGrams,
                    line.TotalMilkMl ?? 0,
                    line.AmountDue.Amount,
                    amountPaid,
                    net,
                    line.AmountDue.Currency,
                    totalGrams > 0 ? line.TotalCoffeeGrams / totalGrams * 100 : 0,
                    line.IsConfirmed,
                    line.Confirmation?.ConfirmedBy.Value,
                    line.Confirmation?.ConfirmedAt,
                    user?.RevolutTag,
                    line.CreditAmount,
                    line.CarriedInAmount,
                    line.AdjustmentsAmount,
                    line.RoundingAmount,
                    line.Resolution?.ToString(),
                    line.CarriedToSettlementId?.Value,
                    settlement.IsTreasurerLine(line),
                    line.MergeTransferAmount ?? 0m,
                    user?.MergedIntoUserId is { } mergedInto ? NameOf(mergedInto) : null);
            })
            .OrderBy(l => l.NetBalance)
            .ThenBy(l => l.UserName)
            .ToList();

        string NameOf(UserId id) => users.TryGetValue(id, out var u) ? u.Name : $"User {id.Value}";

        IReadOnlyList<SettlementTransferDto>? transfers = null;
        if (settlement.IsLedger && settlement.TreasurerUserId is { } treasurerId)
        {
            transfers = SettlementLedgerCalculator
                .StarTransfers(settlement.Lines.Select(l => (l.UserId, l.NetAmount ?? 0m)), treasurerId)
                .Select(t => new SettlementTransferDto(t.From.Value, NameOf(t.From), t.To.Value, NameOf(t.To), t.Amount))
                .ToList();
        }

        return new SettlementDto(
            settlement.Id.Value,
            settlement.SpaceId.Value,
            settlement.BillingPeriodId.Value,
            period.Name,
            period.StartDate,
            period.EndDate,
            settlement.GeneratedAt,
            settlement.GeneratedBy.Value,
            settlement.TotalAmount,
            settlement.Currency,
            settlement.Status.ToString(),
            settlement.CompletedAt,
            settlement.ConfirmedLinesCount,
            settlement.TotalLinesCount,
            lines,
            settlement.Model?.ToString(),
            settlement.TreasurerUserId?.Value,
            settlement.TreasurerUserId is { } t ? NameOf(t) : null,
            settlement.SupersededBySettlementId?.Value,
            transfers);
    }
}
