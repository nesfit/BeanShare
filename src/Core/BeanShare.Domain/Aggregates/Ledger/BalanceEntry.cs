using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Aggregates.Ledger;

/// <summary>
/// A two-party money entry of the space ledger: +<see cref="Amount"/> for <see cref="UserId"/> and
/// −<see cref="Amount"/> for <see cref="CounterpartyUserId"/>. Positive means credit (the member is owed).
/// Editable until a settlement includes it; after that it is locked and a correction is a new entry.
/// </summary>
public sealed class BalanceEntry : AggregateRoot
{
    public const decimal MaxAbsoluteAmount = 1_000_000m;
    public const int MaxNoteLength = 500;

    private BalanceEntry()
    {
        Currency = string.Empty;
        Note = string.Empty;
    }

    public BalanceEntryId Id { get; private set; }
    public SpaceId SpaceId { get; private set; }
    public UserId UserId { get; private set; }
    public UserId CounterpartyUserId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    /// <summary>UTC date (midnight) from which the entry counts.</summary>
    public DateTime EffectiveDate { get; private set; }

    public BalanceEntryKind Kind { get; private set; }
    public string Note { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public UserId? UpdatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public SettlementId? IncludedInSettlementId { get; private set; }
    public int Version { get; private set; }

    public bool IsLocked => IncludedInSettlementId.HasValue;

    public static BalanceEntry Create(
        SpaceId spaceId,
        UserId userId,
        UserId counterpartyUserId,
        decimal amount,
        Currency currency,
        DateTime effectiveDate,
        BalanceEntryKind kind,
        string note,
        UserId createdBy,
        DateTime? ledgerStartDate,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(clock);

        var date = NormalizeDate(effectiveDate);
        Validate(userId, counterpartyUserId, amount, currency, date, kind, note, ledgerStartDate, clock);

        return new BalanceEntry
        {
            Id = BalanceEntryId.New(),
            SpaceId = spaceId,
            UserId = userId,
            CounterpartyUserId = counterpartyUserId,
            Amount = amount,
            Currency = currency.Code,
            EffectiveDate = date,
            Kind = kind,
            Note = note.Trim(),
            CreatedBy = createdBy,
            CreatedAt = clock.UtcNow
        };
    }

    public void Update(
        UserId userId,
        UserId counterpartyUserId,
        decimal amount,
        DateTime effectiveDate,
        BalanceEntryKind kind,
        string note,
        UserId updatedBy,
        DateTime? ledgerStartDate,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureEditable();

        var date = NormalizeDate(effectiveDate);
        Validate(userId, counterpartyUserId, amount, ValueObjects.Currency.Create(Currency), date, kind, note, ledgerStartDate, clock);

        UserId = userId;
        CounterpartyUserId = counterpartyUserId;
        Amount = amount;
        EffectiveDate = date;
        Kind = kind;
        Note = note.Trim();
        UpdatedBy = updatedBy;
        UpdatedAt = clock.UtcNow;
        Version++;
    }

    /// <summary>Throws <see cref="LedgerDomainException"/> (ENTRY_LOCKED) once a settlement has included the entry.</summary>
    public void EnsureEditable()
    {
        if (IsLocked)
            throw new LedgerDomainException(LedgerDomainException.EntryLocked,
                "This balance entry is part of a settlement and can no longer be changed; add a correction entry instead.");
    }

    public void MarkIncluded(SettlementId settlementId)
    {
        EnsureEditable();
        IncludedInSettlementId = settlementId;
        Version++;
    }

    /// <summary>The signed amount seen from <paramref name="userId"/>'s side (0 when not a party).</summary>
    public decimal AmountFor(UserId userId) =>
        userId == UserId ? Amount : userId == CounterpartyUserId ? -Amount : 0m;

    /// <summary>
    /// Checks the entry rules without creating anything; returns the first violation or null.
    /// </summary>
    public static string? GetValidationError(
        UserId userId,
        UserId counterpartyUserId,
        decimal amount,
        Currency currency,
        DateTime effectiveDate,
        BalanceEntryKind kind,
        string? note,
        DateTime? ledgerStartDate,
        IClock clock)
    {
        if (amount == 0)
            return "Amount must not be zero";
        if (Math.Abs(amount) > MaxAbsoluteAmount)
            return $"Amount must be at most {MaxAbsoluteAmount:0} in absolute value";
        if (decimal.Round(amount, currency.DecimalPlaces) != amount)
            return $"Amount can have at most {currency.DecimalPlaces} decimal places";
        if (userId == counterpartyUserId)
            return "The member and the counterparty must differ";
        if (string.IsNullOrWhiteSpace(note))
            return "A note is required";
        if (note.Trim().Length > MaxNoteLength)
            return $"The note can be at most {MaxNoteLength} characters";

        var date = NormalizeDate(effectiveDate);
        var today = clock.UtcNow.Date;
        if (kind == BalanceEntryKind.Opening && ledgerStartDate.HasValue)
        {
            if (date > ledgerStartDate.Value.Date)
                return "An opening entry cannot be dated after the ledger start date";
        }
        else if (date > today)
        {
            return "The effective date cannot be in the future";
        }

        return null;
    }

    public static DateTime NormalizeDate(DateTime date) => UtcDate.Of(date);

    private static void Validate(
        UserId userId,
        UserId counterpartyUserId,
        decimal amount,
        Currency currency,
        DateTime effectiveDate,
        BalanceEntryKind kind,
        string? note,
        DateTime? ledgerStartDate,
        IClock clock)
    {
        var error = GetValidationError(userId, counterpartyUserId, amount, currency, effectiveDate, kind, note, ledgerStartDate, clock);
        if (error is not null)
            throw new ArgumentException(error);
    }
}
