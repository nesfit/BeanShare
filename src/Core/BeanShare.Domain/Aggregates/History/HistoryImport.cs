using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Aggregates.History;

/// <summary>
/// One paste of the Excel history: the cup counts per historical period and member, and
/// the purchases of those periods. Statistics only; money never reads it. The periods themselves are
/// historical billing periods pointing back here, deleted with the import.
/// </summary>
public sealed class HistoryImport : AggregateRoot
{
    public const int MaxNoteLength = 500;

    private readonly List<HistoricalCupCount> _cupCounts = [];
    private readonly List<HistoricalPurchase> _purchases = [];

    private HistoryImport()
    {
        Note = string.Empty;
    }

    public HistoryImportId Id { get; private set; }
    public SpaceId SpaceId { get; private set; }
    public UserId ImportedBy { get; private set; }
    public DateTime ImportedAt { get; private set; }
    public string Note { get; private set; }

    public IReadOnlyCollection<HistoricalCupCount> CupCounts => _cupCounts.AsReadOnly();
    public IReadOnlyCollection<HistoricalPurchase> Purchases => _purchases.AsReadOnly();

    public static HistoryImport Create(SpaceId spaceId, UserId importedBy, string? note, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var trimmed = (note ?? string.Empty).Trim();
        if (trimmed.Length > MaxNoteLength)
            throw new ArgumentException($"The note must be at most {MaxNoteLength} characters");

        return new HistoryImport
        {
            Id = HistoryImportId.New(),
            SpaceId = spaceId,
            ImportedBy = importedBy,
            ImportedAt = clock.UtcNow,
            Note = trimmed
        };
    }

    /// <summary>Adds cups for a member in a period; a second row for the same pair is summed.</summary>
    public void AddCupCount(BillingPeriodId periodId, UserId userId, int cups)
    {
        if (cups < 0)
            throw new ArgumentOutOfRangeException(nameof(cups), "Cups must not be negative");

        var existing = _cupCounts.FirstOrDefault(c => c.BillingPeriodId == periodId && c.UserId == userId);
        if (existing is not null)
        {
            existing.Add(cups);
            return;
        }

        _cupCounts.Add(new HistoricalCupCount(periodId, userId, cups));
    }

    public void AddPurchase(
        BillingPeriodId periodId,
        DateTime? purchasedAt,
        UserId? paidBy,
        string description,
        decimal? grams,
        decimal cost,
        string currency)
    {
        _purchases.Add(new HistoricalPurchase(periodId, purchasedAt, paidBy, description, grams, cost, currency));
    }

    /// <summary>
    /// A merged placeholder's history moves to the target: cup counts summed per period, and the
    /// purchases it paid for. Returns whether anything changed.
    /// </summary>
    public bool MoveMember(UserId from, UserId to)
    {
        var changed = false;

        foreach (var count in _cupCounts.Where(c => c.UserId == from).ToList())
        {
            var target = _cupCounts.FirstOrDefault(c => c.BillingPeriodId == count.BillingPeriodId && c.UserId == to);
            if (target is not null)
            {
                target.Add(count.Cups);
                _cupCounts.Remove(count);
            }
            else
            {
                count.Reassign(to);
            }

            changed = true;
        }

        foreach (var purchase in _purchases.Where(p => p.PaidByUserId == from))
        {
            purchase.ReassignPayer(to);
            changed = true;
        }

        return changed;
    }
}

/// <summary>Cups a member drank in one historical period.</summary>
public sealed class HistoricalCupCount
{
    private HistoricalCupCount() { }

    internal HistoricalCupCount(BillingPeriodId periodId, UserId userId, int cups)
    {
        Id = Guid.NewGuid();
        BillingPeriodId = periodId;
        UserId = userId;
        Cups = cups;
    }

    public Guid Id { get; private set; }
    public BillingPeriodId BillingPeriodId { get; private set; }
    public UserId UserId { get; private set; }
    public int Cups { get; private set; }

    internal void Add(int cups) => Cups += cups;

    internal void Reassign(UserId userId) => UserId = userId;
}

/// <summary>A coffee purchase of a historical period; date and payer may be unknown.</summary>
public sealed class HistoricalPurchase
{
    public const int MaxDescriptionLength = 200;

    private HistoricalPurchase()
    {
        Description = string.Empty;
        Currency = string.Empty;
    }

    internal HistoricalPurchase(
        BillingPeriodId periodId,
        DateTime? purchasedAt,
        UserId? paidBy,
        string description,
        decimal? grams,
        decimal cost,
        string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (description.Length > MaxDescriptionLength)
            throw new ArgumentException($"The description must be at most {MaxDescriptionLength} characters");
        if (grams is <= 0)
            throw new ArgumentOutOfRangeException(nameof(grams), "Grams must be positive");
        if (cost <= 0)
            throw new ArgumentOutOfRangeException(nameof(cost), "Cost must be positive");

        Id = Guid.NewGuid();
        BillingPeriodId = periodId;
        PurchasedAt = purchasedAt.HasValue ? DateTime.SpecifyKind(purchasedAt.Value.Date, DateTimeKind.Utc) : null;
        PaidByUserId = paidBy;
        Description = description.Trim();
        Grams = grams;
        Cost = cost;
        Currency = currency;
    }

    public Guid Id { get; private set; }
    public BillingPeriodId BillingPeriodId { get; private set; }
    public DateTime? PurchasedAt { get; private set; }
    public UserId? PaidByUserId { get; private set; }
    public string Description { get; private set; }
    public decimal? Grams { get; private set; }
    public decimal Cost { get; private set; }
    public string Currency { get; private set; }

    internal void ReassignPayer(UserId userId) => PaidByUserId = userId;
}
