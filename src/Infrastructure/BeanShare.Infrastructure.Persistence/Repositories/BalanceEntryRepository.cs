using BeanShare.Application.Abstractions;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Persistence.Repositories;

public sealed class BalanceEntryRepository : IBalanceEntryRepository
{
    private readonly BeanShareDbContext _context;

    public BalanceEntryRepository(BeanShareDbContext context)
    {
        _context = context;
    }

    public async Task<BalanceEntry?> GetByIdAsync(BalanceEntryId id, CancellationToken cancellationToken = default)
    {
        return await _context.BalanceEntries
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<BalanceEntry>> GetUnincludedForUpdateAsync(SpaceId spaceId, DateTime upTo, CancellationToken cancellationToken = default)
    {
        return await _context.BalanceEntries
            .Where(e => e.SpaceId == spaceId && e.IncludedInSettlementId == null && e.EffectiveDate <= upTo)
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BalanceEntry>> GetVisibleAsync(SpaceId spaceId, UserId? userId, CancellationToken cancellationToken = default)
    {
        var query = _context.BalanceEntries
            .AsNoTracking()
            .Where(e => e.SpaceId == spaceId);

        if (userId is { } id)
        {
            query = query.Where(e => e.UserId == id || e.CounterpartyUserId == id);
        }

        return await query
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(BalanceEntry entry, CancellationToken cancellationToken = default)
    {
        await _context.BalanceEntries.AddAsync(entry, cancellationToken);
    }

    public void Remove(BalanceEntry entry)
    {
        _context.BalanceEntries.Remove(entry);
    }
}
