using BeanShare.Domain.Aggregates.History;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Application.Abstractions;

/// <summary>Excel history imports. Reads are tracked: callers mutate (merge) or remove the result.</summary>
public interface IHistoryImportRepository
{
    Task<HistoryImport?> GetByIdAsync(HistoryImportId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HistoryImport>> GetBySpaceIdAsync(SpaceId spaceId, CancellationToken cancellationToken = default);
    Task AddAsync(HistoryImport import, CancellationToken cancellationToken = default);
    void Remove(HistoryImport import);
}
