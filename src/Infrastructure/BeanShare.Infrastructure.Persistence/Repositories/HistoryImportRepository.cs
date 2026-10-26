using BeanShare.Application.Abstractions;
using BeanShare.Domain.Aggregates.History;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Persistence.Repositories;

public sealed class HistoryImportRepository : IHistoryImportRepository
{
    private readonly BeanShareDbContext _context;

    public HistoryImportRepository(BeanShareDbContext context)
    {
        _context = context;
    }

    public async Task<HistoryImport?> GetByIdAsync(HistoryImportId id, CancellationToken cancellationToken = default)
    {
        return await _context.HistoryImports
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryImport>> GetBySpaceIdAsync(SpaceId spaceId, CancellationToken cancellationToken = default)
    {
        return await _context.HistoryImports
            .Where(h => h.SpaceId == spaceId)
            .OrderBy(h => h.ImportedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(HistoryImport import, CancellationToken cancellationToken = default)
    {
        await _context.HistoryImports.AddAsync(import, cancellationToken);
    }

    public void Remove(HistoryImport import)
    {
        _context.HistoryImports.Remove(import);
    }
}
