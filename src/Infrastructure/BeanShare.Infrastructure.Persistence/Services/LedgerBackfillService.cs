using BeanShare.Application.Features.Settlement;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BeanShare.Infrastructure.Persistence.Services;

/// <summary>
/// Prepares data written before the money ledger (design §7.2): cups already billed by a legacy settlement,
/// or by a settled period without one, get that period's stamp so the ledger never bills them again; legacy
/// lines get their purchase-based "Paid" frozen. Idempotent; runs on every start, after seeding.
/// </summary>
public sealed class LedgerBackfillService
{
    private readonly BeanShareDbContext _context;
    private readonly ILogger<LedgerBackfillService> _logger;

    public LedgerBackfillService(BeanShareDbContext context, ILogger<LedgerBackfillService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var periods = await _context.BillingPeriods.AsNoTracking().ToListAsync(cancellationToken);
        var periodById = periods.ToDictionary(p => p.Id);
        var legacySettlements = await _context.Settlements
            .Include(s => s.Lines)
            .Where(s => s.Model == null)
            .ToListAsync(cancellationToken);
        var periodsWithSettlement = (await _context.Settlements
            .Select(s => s.BillingPeriodId)
            .ToListAsync(cancellationToken)).ToHashSet();

        // 1. Cups a legacy settlement billed by date: in its window and logged before it was generated.
        var stampedByLegacy = 0;
        foreach (var settlement in legacySettlements)
        {
            if (periodById.TryGetValue(settlement.BillingPeriodId, out var period))
                stampedByLegacy += await StampAsync(period, settlement.GeneratedAt, cancellationToken);
        }

        // 2. Settled live periods without a settlement (seed-style data): cups logged before they were settled.
        //    Imported history has no settlement by design and never owns live cups (B26).
        var stampedBySettledPeriod = 0;
        foreach (var period in periods.Where(p => p.State == BillingState.Settled && !p.IsHistorical && !periodsWithSettlement.Contains(p.Id)))
        {
            stampedBySettledPeriod += await StampAsync(period, period.SettledAt ?? period.EndDate, cancellationToken);
        }

        // 3. Freeze the live "Paid" of legacy lines once, so their history no longer moves with purchases.
        var frozenLines = 0;
        foreach (var settlement in legacySettlements.Where(s => s.Lines.Any(l => l.LegacyAmountPaid is null)))
        {
            if (!periodById.TryGetValue(settlement.BillingPeriodId, out var period))
                continue;

            var stock = await _context.CoffeeStocks
                .AsNoTracking()
                .Include(cs => cs.Purchases)
                .FirstOrDefaultAsync(cs => cs.SpaceId == settlement.SpaceId, cancellationToken);
            var paid = LegacyPaid.Compute(stock, period, settlement.Currency);

            foreach (var line in settlement.Lines.Where(l => l.LegacyAmountPaid is null).ToList())
            {
                settlement.SetLegacyAmountPaid(line.UserId, paid.GetValueOrDefault(line.UserId));
                frozenLines++;
            }
        }

        if (stampedByLegacy + stampedBySettledPeriod + frozenLines == 0)
            return;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Ledger backfill: stamped {LegacyCups} cups billed by legacy settlements, {SettledCups} cups of settled periods without a settlement; froze {Lines} legacy line(s)",
            stampedByLegacy, stampedBySettledPeriod, frozenLines);
    }

    private async Task<int> StampAsync(BillingPeriod period, DateTime createdNoLaterThan, CancellationToken cancellationToken)
    {
        var cups = await _context.Consumptions
            .Where(c => c.SpaceId == period.SpaceId
                && c.BillingPeriodId == null
                && c.ConsumedAt >= period.StartDate
                && c.ConsumedAt <= period.EndDate
                && c.CreatedAt <= createdNoLaterThan)
            .ToListAsync(cancellationToken);

        // Another step may already have stamped a tracked cup in this run.
        var unstamped = cups.Where(c => c.BillingPeriodId is null).ToList();
        foreach (var cup in unstamped)
            cup.AssignToBillingPeriod(period.Id);

        return unstamped.Count;
    }
}
