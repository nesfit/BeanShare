using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Aggregates.Settlement;

public sealed class SettlementLine : Entity
{
    private SettlementLine()
    {
        UserId = default!;
        AmountDue = default!;
    }

    public Guid Id { get; private set; }
    public UserId UserId { get; private set; }
    public decimal TotalCoffeeGrams { get; private set; }
    public decimal? TotalMilkMl { get; private set; }
    public Money AmountDue { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public PaymentConfirmation? Confirmation { get; private set; }

    // --- Ledger snapshot (null on legacy lines); amounts never change after creation ---
    public decimal? CreditAmount { get; private set; }
    public decimal? CarriedInAmount { get; private set; }
    public decimal? AdjustmentsAmount { get; private set; }
    public decimal? RoundingAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public LineResolution? Resolution { get; private set; }

    /// <summary>The previous ledger settlement this line's carry-in came from.</summary>
    public SettlementId? CarriedFromSettlementId { get; private set; }

    /// <summary>The settlement this unresolved line was carried into.</summary>
    public SettlementId? CarriedToSettlementId { get; private set; }

    public decimal? CarriedOutAmount { get; private set; }

    /// <summary>Legacy lines only: the purchase-based "Paid" frozen once by the backfill.</summary>
    public decimal? LegacyAmountPaid { get; private set; }

    public bool IsConfirmed => Confirmation is not null;
    public bool IsLedger => Resolution.HasValue;
    public bool IsOpen => Resolution == LineResolution.Open;
    public bool IsCarried => Resolution == LineResolution.Carried;

    public static SettlementLine Create(
        UserId userId,
        decimal totalCoffeeGrams,
        decimal? totalMilkMl,
        Money amountDue,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(amountDue);
        ArgumentNullException.ThrowIfNull(clock);

        if (totalCoffeeGrams < 0)
            throw new ArgumentOutOfRangeException(nameof(totalCoffeeGrams), "Total coffee grams cannot be negative");

        if (totalMilkMl is < 0)
            throw new ArgumentOutOfRangeException(nameof(totalMilkMl), "Total milk ml cannot be negative");

        return new SettlementLine
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TotalCoffeeGrams = totalCoffeeGrams,
            TotalMilkMl = totalMilkMl,
            AmountDue = amountDue,
            CreatedAt = clock.UtcNow
        };
    }

    internal static SettlementLine CreateLedger(
        LedgerLineSnapshot snapshot,
        Currency currency,
        SettlementId? carriedFromSettlementId,
        IClock clock)
    {
        if (snapshot.Cost < 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Cost cannot be negative");

        var line = Create(snapshot.UserId, snapshot.CoffeeGrams, snapshot.MilkMl, Money.Create(snapshot.Cost, currency), clock);
        line.CreditAmount = snapshot.Credit;
        line.CarriedInAmount = snapshot.CarriedIn;
        line.AdjustmentsAmount = snapshot.Adjustments;
        line.RoundingAmount = snapshot.Rounding;
        line.NetAmount = snapshot.Net;
        line.Resolution = LineResolution.Open;
        line.CarriedFromSettlementId = carriedFromSettlementId;
        return line;
    }

    public decimal GetConsumptionPercentage(decimal totalSpaceConsumption)
    {
        if (totalSpaceConsumption <= 0)
            return 0;

        return (TotalCoffeeGrams / totalSpaceConsumption) * 100;
    }

    internal void ConfirmPayment(UserId confirmedBy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(confirmedBy);
        ArgumentNullException.ThrowIfNull(clock);

        if (IsConfirmed)
        {
            throw new InvalidOperationException("Payment has already been confirmed");
        }

        Confirmation = PaymentConfirmation.Create(confirmedBy, clock.UtcNow);
        if (Resolution.HasValue)
            Resolution = LineResolution.Confirmed;
    }

    internal void MarkCarried(SettlementId nextSettlementId, decimal carriedOut)
    {
        if (Resolution != LineResolution.Open)
            throw new InvalidOperationException("Only an open ledger line can be carried");

        Resolution = LineResolution.Carried;
        CarriedToSettlementId = nextSettlementId;
        CarriedOutAmount = carriedOut;
    }

    /// <summary>The treasurer's line resolves itself once every other line is confirmed.</summary>
    internal void ResolveAsTreasurer(UserId treasurerUserId, IClock clock)
    {
        if (Resolution != LineResolution.Open)
            return;

        Confirmation = PaymentConfirmation.Create(treasurerUserId, clock.UtcNow);
        Resolution = LineResolution.Confirmed;
    }

    internal void SetLegacyAmountPaid(decimal amountPaid)
    {
        if (Resolution.HasValue)
            throw new InvalidOperationException("Ledger lines have no legacy paid amount");

        LegacyAmountPaid ??= amountPaid;
    }

    protected override object GetId() => Id;
}
