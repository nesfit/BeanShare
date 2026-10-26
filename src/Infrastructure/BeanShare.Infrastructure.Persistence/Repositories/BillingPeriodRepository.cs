using BeanShare.Application.Abstractions;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Persistence.Repositories;

public sealed class BillingPeriodRepository : IBillingPeriodRepository
{
    private readonly BeanShareDbContext _context;

    public BillingPeriodRepository(BeanShareDbContext context)
    {
        _context = context;
    }

    public async Task<BillingPeriod?> GetByIdAsync(BillingPeriodId id, CancellationToken cancellationToken = default)
    {
        return await _context.BillingPeriods
            .FirstOrDefaultAsync(bp => bp.Id == id, cancellationToken);
    }

    public async Task<BillingPeriod?> GetActiveForSpaceAsync(SpaceId spaceId, CancellationToken cancellationToken = default)
    {
        return await _context.BillingPeriods
            .Where(bp => bp.SpaceId == spaceId && bp.State == BillingState.Open)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BillingPeriod>> GetBySpaceIdAsync(SpaceId spaceId, CancellationToken cancellationToken = default)
    {
        return await _context.BillingPeriods
            .Where(bp => bp.SpaceId == spaceId)
            .OrderByDescending(bp => bp.StartDate)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(BillingPeriod billingPeriod, CancellationToken cancellationToken = default)
    {
        await _context.BillingPeriods.AddAsync(billingPeriod, cancellationToken);
    }

    public Task UpdateAsync(BillingPeriod billingPeriod, CancellationToken cancellationToken = default)
    {
        // BillingPeriod is always loaded within the same DbContext scope before UpdateAsync is
        // called, so it is already tracked. EF Core's snapshot change tracking persists only
        // the modified scalar properties automatically.
        return Task.CompletedTask;
    }

    public void Remove(BillingPeriod billingPeriod)
    {
        _context.BillingPeriods.Remove(billingPeriod);
    }

    public async Task<bool> HasOverlappingPeriodAsync(
        SpaceId spaceId,
        DateTime startDate,
        DateTime endDate,
        BillingPeriodId? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BillingPeriods
            .Where(bp => bp.SpaceId == spaceId);

        if (excludeId != null)
        {
            query = query.Where(bp => bp.Id != excludeId);
        }

        return await query.AnyAsync(bp =>
            (bp.StartDate <= startDate && bp.EndDate >= startDate) ||
            (bp.StartDate <= endDate && bp.EndDate >= endDate) ||
            (bp.StartDate >= startDate && bp.EndDate <= endDate),
            cancellationToken);
    }
}