using System.Linq.Expressions;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Specifications;

/// <summary>
/// The cups a ledger calculation bills. For a settlement of period P: cups stamped to P
/// (any date), or unstamped cups dated up to P's end. For the running balance (no period): the cheap
/// database pre-filter — unstamped, or dated after <c>settledThrough</c>, the end of the latest settled
/// period — after which callers keep only <see cref="IsUnsettled"/> cups. Both modes skip cups dated
/// before the space's ledger start.
/// </summary>
public sealed class LedgerConsumptionsSpecification : ISpec<ConsumptionEntry>
{
    private readonly SpaceId _spaceId;
    private readonly DateTime? _upTo;
    private readonly DateTime? _ledgerStart;
    private readonly BillingPeriodId? _periodId;
    private readonly DateTime? _settledThrough;

    public LedgerConsumptionsSpecification(
        SpaceId spaceId,
        DateTime? upTo,
        DateTime? ledgerStart,
        BillingPeriodId? periodId = null,
        DateTime? settledThrough = null)
    {
        if (periodId.HasValue && !upTo.HasValue)
            throw new ArgumentException("A period calculation needs the period end", nameof(upTo));

        _spaceId = spaceId;
        _upTo = upTo;
        _ledgerStart = ledgerStart;
        _periodId = periodId;
        _settledThrough = settledThrough;
    }

    public Expression<Func<ConsumptionEntry, bool>> Criteria
    {
        get
        {
            var spaceId = _spaceId;
            Expression<Func<ConsumptionEntry, bool>> criteria = c => c.SpaceId == spaceId;

            if (_ledgerStart is { } start)
                criteria = criteria.And(c => c.ConsumedAt >= start);

            if (_periodId is { } periodId)
            {
                BillingPeriodId? stamp = periodId;
                var end = _upTo!.Value;
                criteria = criteria.And(c => c.BillingPeriodId == stamp || (c.BillingPeriodId == null && c.ConsumedAt <= end));
                return criteria;
            }

            if (_upTo is { } upTo)
                criteria = criteria.And(c => c.ConsumedAt <= upTo);

            // Cups stamped to a not-yet-settled period lie after the last settled end, so only
            // unstamped cups need reading from before it.
            if (_settledThrough is { } settledThrough)
                criteria = criteria.And(c => c.BillingPeriodId == null || c.ConsumedAt > settledThrough);

            return criteria;
        }
    }

    public string? Reason => $"Ledger cups in space {_spaceId.Value}" +
        (_periodId.HasValue ? $" for billing period {_periodId.Value.Value}" : " not yet settled") +
        (_ledgerStart.HasValue ? $" from {_ledgerStart.Value:yyyy-MM-dd}" : "") +
        (_settledThrough.HasValue ? $" (stamped cups after {_settledThrough.Value:yyyy-MM-dd})" : "");

    /// <summary>The running-balance rule: not stamped, or stamped to a period that is not settled.</summary>
    public static bool IsUnsettled(ConsumptionEntry cup, IReadOnlySet<BillingPeriodId> settledPeriodIds) =>
        cup.BillingPeriodId is not { } periodId || !settledPeriodIds.Contains(periodId);
}
