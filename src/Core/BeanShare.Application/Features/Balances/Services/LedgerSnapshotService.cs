using BeanShare.Application.Abstractions;
using BeanShare.Domain.Aggregates.BillingPeriod;
using CoffeeStockAggregate = BeanShare.Domain.Aggregates.CoffeeStock.CoffeeStock;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Services;
using BeanShare.Domain.Specifications;
using SettlementAggregate = BeanShare.Domain.Aggregates.Settlement.Settlement;

namespace BeanShare.Application.Features.Balances.Services;

/// <summary>
/// The inputs a ledger calculation used, and its result. <see cref="Cups"/> and <see cref="Entries"/>
/// are what a generation stamps; <see cref="PreviousSettlement"/> is the one it carries forward.
/// </summary>
public sealed record LedgerSnapshot(
    LedgerCalculation Calculation,
    IReadOnlyList<ConsumptionEntry> Cups,
    IReadOnlyList<BalanceEntry> Entries,
    SettlementAggregate? PreviousSettlement,
    IReadOnlyDictionary<UserId, decimal> CarriedIn,
    CoffeeStockAggregate? Stock);

/// <summary>
/// Loads everything a ledger calculation needs and runs <see cref="SettlementLedgerCalculator"/>. Generation,
/// the settlement preview, the unsettled summary and the Balances page all go through here (DS-19).
/// </summary>
public sealed class LedgerSnapshotService
{
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly IBalanceEntryRepository _balanceEntryRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly IClock _clock;

    public LedgerSnapshotService(
        IConsumptionRepository consumptionRepository,
        IBalanceEntryRepository balanceEntryRepository,
        ISettlementRepository settlementRepository,
        ICoffeeStockRepository coffeeStockRepository,
        IBillingPeriodRepository billingPeriodRepository,
        IClock clock)
    {
        _consumptionRepository = consumptionRepository;
        _balanceEntryRepository = balanceEntryRepository;
        _settlementRepository = settlementRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _clock = clock;
    }

    /// <summary>
    /// The settlement of <paramref name="period"/>, with exactly the generation parameters (T20): cups by the
    /// §5.1 predicate up to the period end, entries dated up to it, FallbackRate(end), carry from the latest
    /// ledger settlement. <paramref name="forUpdate"/> loads the cups tracked so they can be stamped.
    /// </summary>
    public async Task<LedgerSnapshot> ForPeriodAsync(Space space, BillingPeriod period, bool forUpdate, CancellationToken cancellationToken)
    {
        var spec = new LedgerConsumptionsSpecification(space.Id, period.EndDate, space.LedgerStartDate, period.Id);
        var cups = forUpdate
            ? await _consumptionRepository.GetBySpecForUpdateAsync(spec, cancellationToken)
            : await _consumptionRepository.GetBySpecAsync(spec, cancellationToken);
        var entries = await _balanceEntryRepository.GetUnincludedForUpdateAsync(space.Id, period.EndDate, cancellationToken);

        return await BuildAsync(space, cups, entries, period.EndDate, cancellationToken);
    }

    /// <summary>The running balance as of now (design §5.3): every unsettled cup and every unincluded entry.</summary>
    public async Task<LedgerSnapshot> RunningAsync(Space space, CancellationToken cancellationToken)
    {
        var periods = await _billingPeriodRepository.GetBySpaceIdAsync(space.Id, cancellationToken);
        var settled = periods.Where(p => p.State == BillingState.Settled).ToList();
        var settledPeriodIds = settled.Select(p => p.Id).ToHashSet();
        var settledThrough = settled.Select(p => (DateTime?)p.EndDate).Max();

        var candidates = await _consumptionRepository.GetBySpecAsync(
            new LedgerConsumptionsSpecification(space.Id, upTo: null, space.LedgerStartDate, settledThrough: settledThrough), cancellationToken);
        var cups = candidates.Where(c => LedgerConsumptionsSpecification.IsUnsettled(c, settledPeriodIds)).ToList();
        var entries = await _balanceEntryRepository.GetUnincludedForUpdateAsync(space.Id, BillingPeriod.OpenEndedSentinel, cancellationToken);

        return await BuildAsync(space, cups, entries, _clock.UtcNow, cancellationToken);
    }

    private async Task<LedgerSnapshot> BuildAsync(
        Space space,
        IReadOnlyList<ConsumptionEntry> cups,
        IReadOnlyList<BalanceEntry> entries,
        DateTime pricedAsOf,
        CancellationToken cancellationToken)
    {
        var previous = await _settlementRepository.GetLatestLedgerForUpdateAsync(space.Id, cancellationToken);
        var stock = await _coffeeStockRepository.GetBySpaceIdAsync(space.Id, cancellationToken);
        var currency = space.Currency;

        var purchases = stock?.Purchases ?? [];
        var bagPayers = purchases.ToDictionary(p => p.Id, p => p.EffectivePayer);
        var fallbackPurchases = purchases
            .Where(p => p.Cost.Currency.Code == currency.Code && p.PurchasedAt <= pricedAsOf)
            .Select(p => new LedgerPurchase(p.EffectivePayer, p.Cost.Amount, p.Quantity.Grams))
            .ToList();

        var ledgerCups = cups
            .Select(c =>
            {
                var stamped = c.CostAmount.HasValue && c.CostCurrency == currency.Code ? c.CostAmount : null;
                UserId? payer = stamped.HasValue && c.StockLotId is { } lotId && bagPayers.TryGetValue(lotId, out var bagPayer)
                    ? bagPayer
                    : null;
                return new LedgerCup(c.UserId, c.Quantity.Grams, stamped, payer);
            })
            .ToList();

        var carriedIn = previous?.GetCarryOut() ?? new Dictionary<UserId, decimal>();
        var calculation = SettlementLedgerCalculator.Calculate(new LedgerCalculationInput(
            ledgerCups,
            fallbackPurchases,
            entries.Select(e => new LedgerEntry(e.UserId, e.CounterpartyUserId, e.Amount)).ToList(),
            carriedIn,
            space.TreasurerUserId,
            currency.DecimalPlaces));

        return new LedgerSnapshot(calculation, cups, entries, previous, carriedIn, stock);
    }
}
