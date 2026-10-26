using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Application.Abstractions;

public interface IBillingPeriodRepository
{
    Task<BillingPeriod?> GetByIdAsync(BillingPeriodId id, CancellationToken cancellationToken = default);
    Task<BillingPeriod?> GetActiveForSpaceAsync(SpaceId spaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BillingPeriod>> GetBySpaceIdAsync(SpaceId spaceId, CancellationToken cancellationToken = default);
    Task AddAsync(BillingPeriod billingPeriod, CancellationToken cancellationToken = default);
    Task UpdateAsync(BillingPeriod billingPeriod, CancellationToken cancellationToken = default);
    void Remove(BillingPeriod billingPeriod);
    Task<bool> HasOverlappingPeriodAsync(SpaceId spaceId, DateTime startDate, DateTime endDate, BillingPeriodId? excludeId = null, CancellationToken cancellationToken = default);
}