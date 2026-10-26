using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Events;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Aggregates.Settlement;

public sealed class Settlement : AggregateRoot
{
    private readonly List<SettlementLine> _lines = new();

    private Settlement() { }

    public SettlementId Id { get; private set; }
    public SpaceId SpaceId { get; private set; }
    public BillingPeriodId BillingPeriodId { get; private set; }
    public DateTime GeneratedAt { get; private set; }
    public UserId GeneratedBy { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal TotalAmount { get; private set; }
    public SettlementStatus Status { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public IReadOnlyCollection<SettlementLine> Lines => _lines.AsReadOnly();

    // --- Ledger (running balance) settlements; all null on legacy settlements ---
    public SettlementModel? Model { get; private set; }

    /// <summary>Generation order within the space; the highest is the latest settlement.</summary>
    public int? Sequence { get; private set; }

    /// <summary>Snapshot of the space's treasurer at generation.</summary>
    public UserId? TreasurerUserId { get; private set; }

    /// <summary>The next settlement that carried this one's unresolved lines.</summary>
    public SettlementId? SupersededBySettlementId { get; private set; }

    /// <summary>Concurrency token, bumped by every line change.</summary>
    public int Version { get; private set; }

    public bool IsLedger => Model == SettlementModel.Ledger;
    public bool AreAllLinesConfirmed => _lines.Count > 0 && _lines.All(l => l.IsConfirmed);
    public int ConfirmedLinesCount => _lines.Count(l => l.IsConfirmed);
    public int TotalLinesCount => _lines.Count;

    public static Settlement Create(
        SpaceId spaceId,
        BillingPeriodId billingPeriodId,
        string currency,
        UserId generatedBy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(spaceId);
        ArgumentNullException.ThrowIfNull(billingPeriodId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentNullException.ThrowIfNull(generatedBy);
        ArgumentNullException.ThrowIfNull(clock);

        var settlement = new Settlement
        {
            Id = new SettlementId(Guid.NewGuid()),
            SpaceId = spaceId,
            BillingPeriodId = billingPeriodId,
            Currency = currency,
            GeneratedBy = generatedBy,
            GeneratedAt = clock.UtcNow,
            TotalAmount = 0,
            Status = SettlementStatus.Generated
        };

        settlement.RaiseDomainEvent(new SettlementGenerated(
            settlement.Id,
            spaceId,
            billingPeriodId,
            generatedBy,
            clock.UtcNow
        ));

        return settlement;
    }

    /// <summary>Creates a running-balance settlement; lines are added with <see cref="AddLedgerLine"/>.</summary>
    public static Settlement CreateLedger(
        SpaceId spaceId,
        BillingPeriodId billingPeriodId,
        string currency,
        UserId generatedBy,
        UserId treasurerUserId,
        int sequence,
        IClock clock)
    {
        if (sequence < 1)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be positive");

        var settlement = Create(spaceId, billingPeriodId, currency, generatedBy, clock);
        settlement.Model = SettlementModel.Ledger;
        settlement.Sequence = sequence;
        settlement.TreasurerUserId = treasurerUserId;
        return settlement;
    }

    public void AddLine(
        UserId userId,
        decimal totalCoffeeGrams,
        decimal amountDue,
        IClock clock,
        decimal? totalMilkMl = null)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(clock);

        if (IsLedger)
        {
            throw new InvalidOperationException("Ledger settlements take snapshot lines (AddLedgerLine)");
        }

        if (totalCoffeeGrams < 0)
        {
            throw new ArgumentException("Total coffee grams cannot be negative", nameof(totalCoffeeGrams));
        }

        if (amountDue < 0)
        {
            throw new ArgumentException("Amount due cannot be negative", nameof(amountDue));
        }

        if (totalMilkMl.HasValue && totalMilkMl.Value < 0)
        {
            throw new ArgumentException("Total milk ml cannot be negative", nameof(totalMilkMl));
        }

        if (_lines.Any(l => l.UserId == userId))
        {
            throw new InvalidOperationException($"Settlement line for user {userId} already exists");
        }

        var line = SettlementLine.Create(
            userId,
            totalCoffeeGrams,
            totalMilkMl,
            Money.Create(amountDue, Currency),
            clock
        );

        _lines.Add(line);

        // Accumulate the rounded line amount so the settlement total always equals the sum
        // of what members are actually billed (each line is rounded to the currency's precision).
        TotalAmount += line.AmountDue.Amount;
    }

    /// <summary>
    /// Adds an immutable snapshot line to a ledger settlement. <paramref name="carriedFromSettlementId"/>
    /// names the previous ledger settlement whose carry this generation consumed.
    /// </summary>
    public SettlementLine AddLedgerLine(LedgerLineSnapshot snapshot, SettlementId? carriedFromSettlementId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(clock);

        if (!IsLedger)
            throw new InvalidOperationException("Only ledger settlements take snapshot lines");

        if (Status != SettlementStatus.Generated)
            throw new InvalidOperationException("Lines can only be added while the settlement is being generated");

        if (_lines.Any(l => l.UserId == snapshot.UserId))
            throw new InvalidOperationException($"Settlement line for user {snapshot.UserId} already exists");

        var line = SettlementLine.CreateLedger(snapshot, Currency, carriedFromSettlementId, clock);
        _lines.Add(line);
        TotalAmount += line.AmountDue.Amount;
        return line;
    }

    public SettlementLine? GetLineForUser(UserId userId)
    {
        return _lines.FirstOrDefault(l => l.UserId == userId);
    }

    public decimal GetAmountDueForUser(UserId userId)
    {
        var line = GetLineForUser(userId);
        return line?.AmountDue.Amount ?? 0;
    }

    public bool HasLineForUser(UserId userId)
    {
        return _lines.Any(l => l.UserId == userId);
    }

    /// <summary>Bumps the token so a racing confirm or generation conflicts (a placeholder merge, DS-32 U6).</summary>
    public void BumpVersion() => Version++;

    public bool IsTreasurerLine(SettlementLine line) => IsLedger && line.UserId == TreasurerUserId;

    /// <summary>
    /// Called after all lines are added to transition to AwaitingConfirmation state
    /// and auto-confirm any zero-amount lines.
    /// </summary>
    public void FinalizeGeneration(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status != SettlementStatus.Generated)
        {
            throw new InvalidOperationException("Settlement must be in Generated status to finalize");
        }

        if (IsLedger)
        {
            // A zero net needs no payment; the treasurer's line resolves only through the others.
            foreach (var line in _lines.Where(l => l.IsOpen && l.NetAmount == 0 && !IsTreasurerLine(l)).ToList())
            {
                line.ConfirmPayment(line.UserId, clock);
            }

            Status = SettlementStatus.AwaitingConfirmation;
            ResolveTreasurerLineIfDone(clock);
            UpdateStatusIfAllConfirmed(clock);
            return;
        }

        // Auto-confirm zero-amount lines (members who owe nothing)
        foreach (var line in _lines.Where(l => l.AmountDue.Amount == 0 && !l.IsConfirmed).ToList())
        {
            line.ConfirmPayment(line.UserId, clock);
        }

        Status = SettlementStatus.AwaitingConfirmation;

        // Check if all lines are already confirmed (e.g., all zero-amount)
        UpdateStatusIfAllConfirmed(clock);
    }

    /// <summary>
    /// Confirms payment for a specific user's settlement line.
    /// Can be called by the member themselves or by an administrator.
    /// </summary>
    public void ConfirmPayment(UserId memberUserId, UserId confirmedBy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(memberUserId);
        ArgumentNullException.ThrowIfNull(confirmedBy);
        ArgumentNullException.ThrowIfNull(clock);

        var line = GetLineForUser(memberUserId);

        if (line is { IsCarried: true })
        {
            throw new LedgerDomainException(LedgerDomainException.LineCarried,
                $"This line was carried into settlement {line.CarriedToSettlementId}; confirm it there");
        }

        if (Status == SettlementStatus.Completed)
        {
            throw new InvalidOperationException("Cannot confirm payment on a completed settlement");
        }

        if (Status == SettlementStatus.Generated)
        {
            throw new InvalidOperationException("Settlement must be finalized before confirming payments");
        }

        if (line is null)
        {
            throw new InvalidOperationException($"No settlement line found for user {memberUserId}");
        }

        if (line.IsConfirmed)
        {
            throw new InvalidOperationException("Payment has already been confirmed");
        }

        if (IsTreasurerLine(line))
        {
            throw new LedgerDomainException(LedgerDomainException.ConfirmationFailed,
                "The treasurer's line resolves itself once every other line is confirmed");
        }

        line.ConfirmPayment(confirmedBy, clock);
        Version++;

        RaiseDomainEvent(new PaymentConfirmed(
            Id,
            SpaceId,
            memberUserId,
            confirmedBy,
            clock.UtcNow
        ));

        if (IsLedger)
            ResolveTreasurerLineIfDone(clock);

        UpdateStatusIfAllConfirmed(clock);
    }

    /// <summary>
    /// Carries this settlement's unresolved lines into <paramref name="nextSettlementId"/> (design §4.2):
    /// an open non-treasurer line carries its full net, a confirmed one nothing, and the treasurer
    /// carries the counter-sum. Returns the non-zero carry-out per user.
    /// </summary>
    public IReadOnlyDictionary<UserId, decimal> CarryForwardTo(SettlementId nextSettlementId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (!IsLedger)
            throw new InvalidOperationException("Only ledger settlements carry forward");

        if (SupersededBySettlementId.HasValue)
            throw new InvalidOperationException($"Settlement {Id} was already carried into {SupersededBySettlementId}");

        var carryOut = GetCarryOut();

        var carriedAny = false;
        foreach (var line in _lines.Where(l => l.IsOpen).ToList())
        {
            line.MarkCarried(nextSettlementId, carryOut.GetValueOrDefault(line.UserId));
            carriedAny = true;
        }

        if (carriedAny)
            SupersededBySettlementId = nextSettlementId;

        Version++;

        return carryOut;
    }

    /// <summary>
    /// What the next settlement would carry in from this one, without changing anything (design §4.2):
    /// the full Net of every open non-treasurer line, and the counter-sum for the treasurer. Zeros omitted.
    /// </summary>
    public IReadOnlyDictionary<UserId, decimal> GetCarryOut()
    {
        if (!IsLedger || SupersededBySettlementId.HasValue)
            return new Dictionary<UserId, decimal>();

        var carryOut = new Dictionary<UserId, decimal>();
        var othersTotal = 0m;
        foreach (var line in _lines.Where(l => l.IsOpen && !IsTreasurerLine(l)))
        {
            var amount = line.NetAmount ?? 0m;
            carryOut[line.UserId] = amount;
            othersTotal += amount;
        }

        if (_lines.FirstOrDefault(IsTreasurerLine) is { } treasurerLine)
            carryOut[treasurerLine.UserId] = -othersTotal;

        return carryOut.Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>Backfill only: freezes the purchase-based Paid of a legacy line.</summary>
    public void SetLegacyAmountPaid(UserId userId, decimal amountPaid)
    {
        if (IsLedger)
            throw new InvalidOperationException("Ledger settlements have no legacy paid amounts");

        var line = GetLineForUser(userId)
            ?? throw new InvalidOperationException($"No settlement line found for user {userId}");

        if (line.LegacyAmountPaid.HasValue)
            return;

        line.SetLegacyAmountPaid(amountPaid);
        Version++;
    }

    private void ResolveTreasurerLineIfDone(IClock clock)
    {
        var treasurerLine = _lines.FirstOrDefault(IsTreasurerLine);
        if (treasurerLine is not { IsOpen: true } || TreasurerUserId is not { } treasurer)
            return;

        if (_lines.Where(l => !IsTreasurerLine(l)).All(l => l.Resolution == LineResolution.Confirmed))
            treasurerLine.ResolveAsTreasurer(treasurer, clock);
    }

    private void UpdateStatusIfAllConfirmed(IClock clock)
    {
        var allResolved = IsLedger
            ? _lines.All(l => l.IsConfirmed)
            : AreAllLinesConfirmed;

        if (allResolved && Status != SettlementStatus.Completed)
        {
            Status = SettlementStatus.Completed;
            CompletedAt = clock.UtcNow;

            RaiseDomainEvent(new SettlementCompleted(
                Id,
                SpaceId,
                BillingPeriodId,
                clock.UtcNow
            ));
        }
    }
}
