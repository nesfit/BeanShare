using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Events;
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

    public void AddLine(
        UserId userId,
        decimal totalCoffeeGrams,
        decimal amountDue,
        IClock clock,
        decimal? totalMilkMl = null)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(clock);

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

        if (Status == SettlementStatus.Completed)
        {
            throw new InvalidOperationException("Cannot confirm payment on a completed settlement");
        }

        if (Status == SettlementStatus.Generated)
        {
            throw new InvalidOperationException("Settlement must be finalized before confirming payments");
        }

        var line = GetLineForUser(memberUserId);
        if (line is null)
        {
            throw new InvalidOperationException($"No settlement line found for user {memberUserId}");
        }

        if (line.IsConfirmed)
        {
            throw new InvalidOperationException("Payment has already been confirmed");
        }

        line.ConfirmPayment(confirmedBy, clock);

        RaiseDomainEvent(new PaymentConfirmed(
            Id,
            SpaceId,
            memberUserId,
            confirmedBy,
            clock.UtcNow
        ));

        UpdateStatusIfAllConfirmed(clock);
    }

    private void UpdateStatusIfAllConfirmed(IClock clock)
    {
        if (AreAllLinesConfirmed && Status != SettlementStatus.Completed)
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