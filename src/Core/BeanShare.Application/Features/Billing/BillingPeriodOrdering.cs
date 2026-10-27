using BeanShare.Application.Common;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Enums;

namespace BeanShare.Application.Features.Billing;

/// <summary>A live period may only start after the end of the latest settled live period; history is ignored.</summary>
internal static class BillingPeriodOrdering
{
    public static Error? CheckStartsAfterLastSettled(DateTime startDate, IEnumerable<BillingPeriod> spacePeriods)
    {
        var lastSettledEnd = spacePeriods
            .Where(p => p.State == BillingState.Settled && !p.IsHistorical)
            .Select(p => (DateTime?)p.EndDate)
            .Max();

        return lastSettledEnd is { } end && startDate.Date <= end
            ? Error.SettlementOrder($"A billing period must start after the last settled period, which ended {end:yyyy-MM-dd}")
            : null;
    }
}
