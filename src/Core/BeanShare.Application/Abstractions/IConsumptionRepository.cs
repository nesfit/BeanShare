using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Application.Abstractions;

public interface IConsumptionRepository
{
    Task AddAsync(ConsumptionEntry consumption, CancellationToken cancellationToken = default);
    Task<ConsumptionEntry?> GetByIdAsync(ConsumptionEntryId id, CancellationToken cancellationToken = default);
    Task<IEnumerable<ConsumptionEntry>> GetBySpaceIdAsync(SpaceId spaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConsumptionEntry>> GetRecentBySpaceIdAsync(SpaceId spaceId, int limit, UserId? forUserId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ConsumptionEntry>> GetBySpecAsync(ISpec<ConsumptionEntry> specification, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="GetBySpecAsync"/>, but tracked, so changes (e.g. period stamps) are saved.</summary>
    Task<IReadOnlyList<ConsumptionEntry>> GetBySpecForUpdateAsync(ISpec<ConsumptionEntry> specification, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<ConsumptionEntry> Items, int TotalCount)> GetPagedBySpecAsync(ISpec<ConsumptionEntry> specification, int skip, int take, CancellationToken cancellationToken = default);
    Task<(decimal TotalGrams, int TotalEntries, int UniqueDays)> GetSummaryBySpecAsync(ISpec<ConsumptionEntry> specification, CancellationToken cancellationToken = default);
    Task UpdateAsync(ConsumptionEntry consumption, CancellationToken cancellationToken = default);
    Task RemoveAsync(ConsumptionEntry consumption, CancellationToken cancellationToken = default);
}