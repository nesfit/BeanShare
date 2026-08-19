using System.Linq.Expressions;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Specifications;

/// <summary>
/// Cups in a space that may still be waiting to be settled: everything logged from
/// <c>notBefore</c> onwards, plus every cup that was never assigned to a billing period
/// (back-dated entries logged after a period was already closed land there).
///
/// This is deliberately a superset — narrowing it to the exact unsettled set means dropping
/// cups whose billing period is already <c>Settled</c>, which would need an IN-list over
/// converted ids in SQL. The cheap date/null pre-filter runs in the database and the caller
/// applies the exact rule over the (small) remainder.
/// </summary>
public sealed class UnsettledConsumptionsSpecification : ISpec<ConsumptionEntry>
{
    private readonly SpaceId _spaceId;
    private readonly DateTime? _notBefore;

    /// <param name="notBefore">
    /// Watermark of the last settlement. <c>null</c> means the space has never been settled, so
    /// every cup is a candidate.
    /// </param>
    public UnsettledConsumptionsSpecification(SpaceId spaceId, DateTime? notBefore = null)
    {
        ArgumentNullException.ThrowIfNull(spaceId);

        _spaceId = spaceId;
        _notBefore = notBefore;
    }

    public Expression<Func<ConsumptionEntry, bool>> Criteria => c =>
        c.SpaceId == _spaceId &&
        (_notBefore == null || c.ConsumedAt >= _notBefore.Value || c.BillingPeriodId == null);

    public string? Reason => $"Potentially unsettled consumptions in space {_spaceId.Value}" +
        (_notBefore.HasValue ? $" since {_notBefore.Value:yyyy-MM-dd}" : " (never settled)");
}
