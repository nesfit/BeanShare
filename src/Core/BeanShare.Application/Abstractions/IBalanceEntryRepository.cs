using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Application.Abstractions;

public interface IBalanceEntryRepository
{
    /// <summary>Tracked: the caller may update or remove the entry.</summary>
    Task<BalanceEntry?> GetByIdAsync(BalanceEntryId id, CancellationToken cancellationToken = default);

    /// <summary>Tracked entries no settlement has included yet, dated on or before <paramref name="upTo"/>.</summary>
    Task<IReadOnlyList<BalanceEntry>> GetUnincludedForUpdateAsync(SpaceId spaceId, DateTime upTo, CancellationToken cancellationToken = default);

    /// <summary>All entries of the space, or only those naming <paramref name="userId"/> as either party.</summary>
    Task<IReadOnlyList<BalanceEntry>> GetVisibleAsync(SpaceId spaceId, UserId? userId, CancellationToken cancellationToken = default);

    Task AddAsync(BalanceEntry entry, CancellationToken cancellationToken = default);

    void Remove(BalanceEntry entry);
}
