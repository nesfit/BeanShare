using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Common;

namespace BeanShare.Application.Features.Balances.Services;

internal static class BalanceEntryMapping
{
    public static async Task<IReadOnlyList<BalanceEntryDto>> ToDtosAsync(
        IReadOnlyCollection<BalanceEntry> entries,
        UserId viewer,
        IUserService userService,
        CancellationToken cancellationToken)
    {
        var userIds = entries.SelectMany(e => new[] { e.UserId, e.CounterpartyUserId }).Distinct();
        var names = (await userService.GetByIdsAsync(userIds, cancellationToken))
            .ToDictionary(u => u.Id, u => u.Name);

        return entries.Select(e => ToDto(e, viewer, names)).ToList();
    }

    public static BalanceEntryDto ToDto(BalanceEntry entry, UserId viewer, IReadOnlyDictionary<UserId, string> names)
    {
        var isParty = viewer == entry.UserId || viewer == entry.CounterpartyUserId;
        return new BalanceEntryDto(
            entry.Id.Value,
            entry.SpaceId.Value,
            entry.UserId.Value,
            names.GetValueOrDefault(entry.UserId, "Unknown"),
            entry.CounterpartyUserId.Value,
            names.GetValueOrDefault(entry.CounterpartyUserId, "Unknown"),
            entry.Amount,
            entry.Currency,
            entry.EffectiveDate,
            entry.Kind.ToString(),
            entry.Note,
            entry.CreatedBy.Value,
            entry.CreatedAt,
            entry.UpdatedAt,
            entry.IncludedInSettlementId?.Value,
            entry.IsLocked,
            isParty ? entry.AmountFor(viewer) : null);
    }
}
