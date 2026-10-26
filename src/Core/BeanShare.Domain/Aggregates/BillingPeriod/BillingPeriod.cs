using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Events;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Aggregates.BillingPeriod;

public sealed class BillingPeriod : AggregateRoot
{
    private BillingPeriod() { }

    public BillingPeriodId Id { get; private set; }
    public SpaceId SpaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public BillingState State { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public DateTime? SettledAt { get; private set; }
    public UserId CreatedBy { get; private set; }
    public UserId? ClosedBy { get; private set; }
    public UserId? SettledBy { get; private set; }

    /// <summary>Concurrency token, bumped by every state transition.</summary>
    public int Version { get; private set; }

    public static readonly DateTime OpenEndedSentinel = new(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc);

    public bool IsOpenEnded => EndDate >= OpenEndedSentinel.Date;

    public static BillingPeriod Create(
        SpaceId spaceId,
        string name,
        DateTime startDate,
        DateTime? endDate,
        UserId createdBy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(createdBy);
        ArgumentNullException.ThrowIfNull(clock);

        if (startDate.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Start date must be in UTC");

        DateTime resolvedEnd;
        if (endDate.HasValue)
        {
            if (endDate.Value.Kind != DateTimeKind.Utc)
                throw new ArgumentException("End date must be in UTC");
            if (endDate.Value <= startDate)
                throw new ArgumentException("End date must be after start date");
            resolvedEnd = endDate.Value.Date.AddDays(1).AddTicks(-1);
        }
        else
        {
            resolvedEnd = OpenEndedSentinel;
        }

        var billingPeriod = new BillingPeriod
        {
            Id = new BillingPeriodId(Guid.NewGuid()),
            SpaceId = spaceId,
            Name = name,
            StartDate = startDate.Date,
            EndDate = resolvedEnd,
            State = BillingState.Draft,
            CreatedAt = clock.UtcNow,
            CreatedBy = createdBy
        };

        billingPeriod.RaiseDomainEvent(new BillingPeriodCreated(
            billingPeriod.Id,
            spaceId,
            name,
            startDate,
            endDate ?? resolvedEnd,
            createdBy,
            clock.UtcNow
        ));

        return billingPeriod;
    }

    public void Open(UserId userId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(clock);

        if (State != BillingState.Draft)
        {
            throw new InvalidOperationException($"Cannot open billing period in state {State}");
        }

        State = BillingState.Open;
        Version++;

        RaiseDomainEvent(new BillingPeriodOpened(
            Id,
            SpaceId,
            userId,
            clock.UtcNow
        ));
    }

    public void Close(UserId userId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(clock);

        if (State != BillingState.Open)
        {
            throw new InvalidOperationException($"Cannot close billing period in state {State}");
        }

        State = BillingState.Closed;
        ClosedAt = clock.UtcNow;
        ClosedBy = userId;
        Version++;

        if (IsOpenEnded)
            EndDate = clock.UtcNow.Date.AddDays(1).AddTicks(-1);

        RaiseDomainEvent(new BillingPeriodClosed(
            Id,
            SpaceId,
            userId,
            clock.UtcNow
        ));
    }

    /// <summary>
    /// Reopens a closed period. The end date goes back to open-ended only when no later period exists;
    /// otherwise it stays, so periods never overlap (a later re-close would end today).
    /// </summary>
    public void Reopen(UserId userId, IClock clock, bool hasLaterPeriod = false)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(clock);

        if (State != BillingState.Closed)
        {
            throw new InvalidOperationException($"Cannot reopen billing period in state {State}");
        }

        State = BillingState.Open;
        ClosedAt = null;
        ClosedBy = null;
        if (!hasLaterPeriod)
            EndDate = OpenEndedSentinel;
        Version++;

        RaiseDomainEvent(new BillingPeriodOpened(
            Id,
            SpaceId,
            userId,
            clock.UtcNow
        ));
    }

    public void MarkAsSettled(UserId userId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(clock);

        if (State != BillingState.Closed)
        {
            throw new InvalidOperationException($"Cannot settle billing period in state {State}");
        }

        State = BillingState.Settled;
        SettledAt = clock.UtcNow;
        SettledBy = userId;
        Version++;

        RaiseDomainEvent(new BillingPeriodSettled(
            Id,
            SpaceId,
            userId,
            clock.UtcNow
        ));
    }

    public bool ContainsDate(DateTime date)
    {
        // Use the date as-is - callers should provide UTC dates consistent with StartDate/EndDate
        return date >= StartDate && date <= EndDate;
    }

    public bool OverlapsWith(DateTime startDate, DateTime endDate)
    {
        return startDate < EndDate && endDate > StartDate;
    }

    public bool CanBeOpened()
    {
        return State == BillingState.Draft;
    }

    public bool CanBeClosed()
    {
        return State == BillingState.Open;
    }

    public bool CanBeSettled()
    {
        return State == BillingState.Closed;
    }

    public bool CanBeReopened()
    {
        return State == BillingState.Closed;
    }
}