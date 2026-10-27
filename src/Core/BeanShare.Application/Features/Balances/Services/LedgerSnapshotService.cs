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
/// <param name="FallbackRateMissing">
/// Some cup is priced by the fallback rate, but the fallback set has no grams or no cost, so those cups
/// cost nothing (H5).
/// </param>
public sealed record LedgerSnapshot(
    LedgerCalculation Calculation,
    IReadOnlyList<ConsumptionEntry> Cups,
    IReadOnlyList<BalanceEntry> Entries,
    SettlementAggregate? PreviousSettlement,
    IReadOnlyDictionary<UserId, decimal> CarriedIn,
    CoffeeStockAggregate? Stock,
    bool FallbackRateMissing = false);

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
    private readonly IUserRepository _userRepository;

    public LedgerSnapshotService(
        IConsumptionRepository consumptionRepository,
        IBalanceEntryRepository balanceEntryRepository,
        ISettlementRepository settlementRepository,
        ICoffeeStockRepository coffeeStockRepository,
        IBillingPeriodRepository billingPeriodRepository,
        IClock clock,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
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
        // Live periods only: imported history is statistics and must not move the watermark (B26).
        var settled = periods.Where(p => p.State == BillingState.Settled && !p.IsHistorical).ToList();
        var settledPeriodIds = settled.Select(p => p.Id).ToHashSet();
        var settledThrough = settled.Select(p => (DateTime?)p.EndDate).Max();
        // The stamped-cup pre-filter below assumes no unsettled live period starts on or before that end. Should two live
        // periods ever overlap, cap the cut-off before the earliest unsettled one, so its cups are still read.
        var earliestUnsettledStart = periods.Where(p => p.State != BillingState.Settled && !p.IsHistorical)
            .Select(p => (DateTime?)p.StartDate).Min();
        if (settledThrough is { } through && earliestUnsettledStart is { } unsettledStart && unsettledStart <= through)
            settledThrough = unsettledStart.AddTicks(-1);

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

        // "Paid in Excel" bags earn no BeanShare credit and stay out of the fallback price (DS-41).
        var purchases = stock?.Purchases ?? [];
        var paidInExcel = purchases.Where(p => p.ExcludedFromLedgerCredit).Select(p => p.Id).ToHashSet();
        var bagPayers = purchases.ToDictionary(p => p.Id, p => p.EffectivePayer);
        var fallbackPurchases = purchases
            .Where(p => !p.ExcludedFromLedgerCredit && p.Cost.Currency.Code == currency.Code && p.PurchasedAt <= pricedAsOf)
            .Select(p => new LedgerPurchase(p.EffectivePayer, p.Cost.Amount, p.Quantity.Grams))
            .ToList();

        var ledgerCups = cups
            .Select(c =>
            {
                // A cup stamped to a flagged bag is priced as a fallback cup; the cup itself is unchanged.
                if (c.StockLotId is { } flaggedLot && paidInExcel.Contains(flaggedLot))
                    return new LedgerCup(c.UserId, c.Quantity.Grams, null, null);

                var stamped = c.CostAmount.HasValue && c.CostCurrency == currency.Code ? c.CostAmount : null;
                UserId? payer = stamped.HasValue && c.StockLotId is { } lotId && bagPayers.TryGetValue(lotId, out var bagPayer)
                    ? bagPayer
                    : null;
                return new LedgerCup(c.UserId, c.Quantity.Grams, stamped, payer);
            })
            .ToList();

        var carriedIn = previous?.GetCarryOut() ?? new Dictionary<UserId, decimal>();
        var ledgerEntries = entries.Select(e => new LedgerEntry(e.UserId, e.CounterpartyUserId, e.Amount)).ToList();

        // Every user the calculation can touch, member or not (W1), read uncached (DS-30).
        var inputUsers = carriedIn.Keys
            .Concat(ledgerCups.Select(c => c.Drinker))
            .Concat(ledgerCups.Where(c => c.Payer.HasValue).Select(c => c.Payer!.Value))
            .Concat(ledgerEntries.SelectMany(e => new[] { e.UserId, e.CounterpartyUserId }))
            .Concat(fallbackPurchases.Select(p => p.Payer))
            .ToHashSet();
        var mergedInto = await _userRepository.GetMergedMapAsync(inputUsers, cancellationToken)
            ?? new Dictionary<UserId, UserId>();

        var calculation = SettlementLedgerCalculator.Calculate(new LedgerCalculationInput(
            ledgerCups,
            fallbackPurchases,
            ledgerEntries,
            carriedIn,
            space.TreasurerUserId,
            currency.DecimalPlaces,
            mergedInto));

        var fallbackRateMissing = ledgerCups.Any(c => c.StampedCost is null)
            && (fallbackPurchases.Sum(p => p.Grams) <= 0 || fallbackPurchases.Sum(p => p.Cost) <= 0);

        return new LedgerSnapshot(calculation, cups, entries, previous, carriedIn, stock, fallbackRateMissing);
    }
}
