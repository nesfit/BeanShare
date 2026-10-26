using BeanShare.Domain.Aggregates.Settlement;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Application.Abstractions;

public interface ISettlementRepository
{
    Task<Settlement?> GetByIdAsync(SettlementId id, CancellationToken cancellationToken = default);
    Task<Settlement?> GetByBillingPeriodIdAsync(BillingPeriodId billingPeriodId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Settlement>> GetBySpaceIdAsync(SpaceId spaceId, CancellationToken cancellationToken = default);
    Task AddAsync(Settlement settlement, CancellationToken cancellationToken = default);
    Task UpdateAsync(Settlement settlement, CancellationToken cancellationToken = default);

    /// <summary>Tracked: the ledger settlement with the highest sequence in the space, or null.</summary>
    Task<Settlement?> GetLatestLedgerForUpdateAsync(SpaceId spaceId, CancellationToken cancellationToken = default);

    /// <summary>The highest settlement sequence in the space (0 when none).</summary>
    Task<int> GetMaxSequenceAsync(SpaceId spaceId, CancellationToken cancellationToken = default);

    Task<bool> HasLedgerSettlementAsync(SpaceId spaceId, CancellationToken cancellationToken = default);
}