using BeanShare.Application.Abstractions;
using BeanShare.Domain.Aggregates.Settlement;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Persistence.Repositories;

public sealed class SettlementRepository : ISettlementRepository
{
    private readonly BeanShareDbContext _context;

    public SettlementRepository(BeanShareDbContext context)
    {
        _context = context;
    }

    public async Task<Settlement?> GetByIdAsync(SettlementId id, CancellationToken cancellationToken = default)
    {
        return await _context.Settlements
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Settlement?> GetByBillingPeriodIdAsync(BillingPeriodId billingPeriodId, CancellationToken cancellationToken = default)
    {
        return await _context.Settlements
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.BillingPeriodId == billingPeriodId, cancellationToken);
    }

    public async Task<IReadOnlyList<Settlement>> GetBySpaceIdAsync(SpaceId spaceId, CancellationToken cancellationToken = default)
    {
        return await _context.Settlements
            .Include(s => s.Lines)
            .Where(s => s.SpaceId == spaceId)
            .OrderByDescending(s => s.GeneratedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Settlement settlement, CancellationToken cancellationToken = default)
    {
        await _context.Settlements.AddAsync(settlement, cancellationToken);
    }

    public async Task<Settlement?> GetLatestLedgerForUpdateAsync(SpaceId spaceId, CancellationToken cancellationToken = default)
    {
        return await _context.Settlements
            .Include(s => s.Lines)
            .Where(s => s.SpaceId == spaceId && s.Model == SettlementModel.Ledger)
            .OrderByDescending(s => s.Sequence)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> GetMaxSequenceAsync(SpaceId spaceId, CancellationToken cancellationToken = default)
    {
        return await _context.Settlements
            .Where(s => s.SpaceId == spaceId && s.Sequence != null)
            .MaxAsync(s => s.Sequence, cancellationToken) ?? 0;
    }

    public async Task<bool> HasLedgerSettlementAsync(SpaceId spaceId, CancellationToken cancellationToken = default)
    {
        return await _context.Settlements
            .AnyAsync(s => s.SpaceId == spaceId && s.Model == SettlementModel.Ledger, cancellationToken);
    }

    public Task UpdateAsync(Settlement settlement, CancellationToken cancellationToken = default)
    {
        // Settlement is always loaded within the same DbContext scope before UpdateAsync is
        // called, so it is already tracked. EF Core's snapshot change tracking handles all
        // mutations to scalar properties and to the OwnsMany Lines collection automatically.
        return Task.CompletedTask;
    }
}