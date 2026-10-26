using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Persistence.Repositories;

internal sealed class ConsumptionRepository : IConsumptionRepository
{
    private readonly BeanShareDbContext _context;

    public ConsumptionRepository(BeanShareDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ConsumptionEntry consumption, CancellationToken cancellationToken = default)
    {
        await _context.Consumptions.AddAsync(consumption, cancellationToken);
    }

    public async Task<ConsumptionEntry?> GetByIdAsync(ConsumptionEntryId id, CancellationToken cancellationToken = default)
    {
        return await _context.Consumptions
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<ConsumptionEntry>> GetBySpaceIdAsync(SpaceId spaceId, CancellationToken cancellationToken = default)
    {
        var specification = new ConsumptionBySpaceSpecification(spaceId);
        return await _context.Consumptions
            .AsNoTracking()
            .Where(specification.Criteria)
            .OrderByDescending(c => c.ConsumedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConsumptionEntry>> GetRecentBySpaceIdAsync(SpaceId spaceId, int limit, UserId? forUserId = null, CancellationToken ct = default)
    {
        var query = _context.Consumptions
            .AsNoTracking()
            .Where(c => c.SpaceId == spaceId);

        if (forUserId != null)
        {
            query = query.Where(c => c.UserId == forUserId);
        }

        return await query
            .OrderByDescending(c => c.ConsumedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ConsumptionEntry>> GetBySpecAsync(ISpec<ConsumptionEntry> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return await _context.Consumptions
            .AsNoTracking()
            .Where(specification.Criteria)
            .OrderByDescending(c => c.ConsumedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConsumptionEntry>> GetBySpecForUpdateAsync(ISpec<ConsumptionEntry> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        // Tracked: callers stamp the cups (B1).
        return await _context.Consumptions
            .Where(specification.Criteria)
            .OrderBy(c => c.ConsumedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<ConsumptionEntry> Items, int TotalCount)> GetPagedBySpecAsync(ISpec<ConsumptionEntry> specification, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        var baseQuery = _context.Consumptions
            .AsNoTracking()
            .Where(specification.Criteria);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var items = await baseQuery
            .OrderByDescending(c => c.ConsumedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(decimal TotalGrams, int TotalEntries, int UniqueDays)> GetSummaryBySpecAsync(ISpec<ConsumptionEntry> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        var baseQuery = _context.Consumptions
            .AsNoTracking()
            .Where(specification.Criteria);

        var totalEntries = await baseQuery.CountAsync(cancellationToken);

        if (totalEntries == 0)
        {
            return (0, 0, 0);
        }

        var totalGrams = await baseQuery.SumAsync(c => c.Quantity.Grams, cancellationToken);
        var uniqueDays = await baseQuery.Select(c => c.ConsumedAt.Date).Distinct().CountAsync(cancellationToken);

        return (totalGrams, totalEntries, uniqueDays);
    }

    public Task UpdateAsync(ConsumptionEntry consumption, CancellationToken cancellationToken = default)
    {
        // ConsumptionEntry is always loaded within the same DbContext scope before UpdateAsync
        // is called, so it is already tracked. EF Core's snapshot change tracking persists
        // only the modified scalar properties automatically.
        return Task.CompletedTask;
    }

    public Task RemoveAsync(ConsumptionEntry consumption, CancellationToken cancellationToken = default)
    {
        _context.Consumptions.Remove(consumption);
        return Task.CompletedTask;
    }
}