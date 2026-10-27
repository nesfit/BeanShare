using BeanShare.Domain.Common;
using BeanShare.Domain.Events;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Entities;

public sealed class ConsumptionEntry : AggregateRoot
{
    public ConsumptionEntryId Id { get; private set; }
    public SpaceId SpaceId { get; private set; }
    public UserId UserId { get; private set; }
    public CoffeeProduct Product { get; private set; }
    public Weight Quantity { get; private set; }
    public DateTime ConsumedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public BillingPeriodId? BillingPeriodId { get; private set; }
    public PresetRecipeId? PresetId { get; private set; }
    public string? PresetName { get; private set; }

    // --- Per-bag cost lock-in (persisted as scalars; Money is rebuilt in the computed Cost below,
    // because an all-null owned Money would mis-materialize) ---
    /// <summary>The specific bag (Purchase lot) this cup was drawn from; null for legacy/imported entries.</summary>
    public Guid? StockLotId { get; private set; }
    public decimal? CostAmount { get; private set; }
    public string? CostCurrency { get; private set; }
    /// <summary>The bag's per-gram price at log time (for display/audit); null when unattributed.</summary>
    public decimal? UnitCostPerGram { get; private set; }

    /// <summary>
    /// True for a phantom-fill cup: this user's share of a bag's untracked remainder, billed when an
    /// admin marked the bag empty — not a cup they logged. Lets the emptying be undone as a whole.
    /// </summary>
    public bool IsWriteOff { get; private set; }

    /// <summary>Concurrency token, bumped by every change to the cup.</summary>
    public int Version { get; private set; }

    /// <summary>Cost of this cup, stamped at log time from the bag's price; null when it couldn't be attributed.</summary>
    public Money? Cost => CostAmount.HasValue && !string.IsNullOrEmpty(CostCurrency)
        ? Money.Create(CostAmount.Value, Currency.Create(CostCurrency))
        : null;

    private ConsumptionEntry()
    {
        Id = default!;
        SpaceId = default;
        UserId = default;
        Product = CoffeeProduct.Create("Unknown", "Unknown", CoffeeType.Espresso);
        Quantity = default!;
    }

    private ConsumptionEntry(
        ConsumptionEntryId id,
        SpaceId spaceId,
        UserId userId,
        CoffeeProduct product,
        Weight quantity,
        DateTime consumedAt,
        DateTime createdAt,
        PresetRecipeId? presetId = null,
        string? presetName = null)
    {
        Id = id;
        SpaceId = spaceId;
        UserId = userId;
        Product = product;
        Quantity = quantity;
        ConsumedAt = consumedAt;
        CreatedAt = createdAt;
        PresetId = presetId;
        PresetName = presetName;
    }

    public static ConsumptionEntry Create(
        SpaceId spaceId,
        UserId userId,
        CoffeeProduct product,
        Weight quantity,
        DateTime consumedAt,
        IClock clock,
        PresetRecipeId? presetId = null,
        string? presetName = null,
        Guid? stockLotId = null,
        Money? cost = null,
        decimal? unitCostPerGram = null,
        bool isWriteOff = false)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(clock);

        if (quantity.IsZero || !quantity.IsPositive)
        {
            throw new ArgumentException("Consumption quantity must be positive", nameof(quantity));
        }

        // Allow a small tolerance of 5 minutes for timezone conversion and clock drift
        if (consumedAt > clock.UtcNow.AddMinutes(5))
        {
            throw new ArgumentException("Consumption time cannot be in the future", nameof(consumedAt));
        }

        var id = ConsumptionEntryId.New();
        var createdAt = clock.UtcNow;

        var entry = new ConsumptionEntry(id, spaceId, userId, product, quantity, consumedAt, createdAt, presetId, presetName)
        {
            StockLotId = stockLotId,
            CostAmount = cost?.Amount,
            CostCurrency = cost?.Currency.Code,
            UnitCostPerGram = unitCostPerGram,
            IsWriteOff = isWriteOff
        };

        entry.RaiseDomainEvent(new ConsumptionRecorded(
            entry.Id.Value,
            entry.SpaceId.Value,
            entry.UserId.Value,
            entry.Product,
            entry.Quantity,
            entry.ConsumedAt,
            clock.UtcNow));

        return entry;
    }

    public void Update(
        CoffeeProduct product,
        Weight quantity,
        DateTime consumedAt,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(clock);

        if (quantity.IsZero || !quantity.IsPositive)
        {
            throw new ArgumentException("Consumption quantity must be positive", nameof(quantity));
        }

        if (consumedAt > clock.UtcNow.AddMinutes(5))
        {
            throw new ArgumentException("Consumption time cannot be in the future", nameof(consumedAt));
        }

        Product = product;
        Quantity = quantity;
        ConsumedAt = consumedAt;
        Version++;
    }

    /// <summary>Stamp which bag this cup drew from and the cost taken from that bag's price.</summary>
    public void SetAllocation(Guid? stockLotId, Money? cost, decimal? unitCostPerGram)
    {
        StockLotId = stockLotId;
        CostAmount = cost?.Amount;
        CostCurrency = cost?.Currency.Code;
        UnitCostPerGram = unitCostPerGram;
        Version++;
    }

    /// <summary>Drop the bag/cost stamp (e.g. before re-allocating on edit, or when unattributed).</summary>
    public void ClearAllocation()
    {
        StockLotId = null;
        CostAmount = null;
        CostCurrency = null;
        UnitCostPerGram = null;
        Version++;
    }

    /// <summary>
    /// Drops the period stamp of a cup re-dated out of its (still open) period, so the next close stamps
    /// it again by date. The caller ensures the stamped period is neither Closed nor Settled.
    /// </summary>
    public void ClearBillingPeriod()
    {
        if (BillingPeriodId is null)
            return;

        BillingPeriodId = null;
        Version++;
    }

    public void AssignToBillingPeriod(BillingPeriodId billingPeriodId)
    {
        ArgumentNullException.ThrowIfNull(billingPeriodId);

        if (BillingPeriodId != null)
        {
            throw new InvalidOperationException("Consumption is already assigned to a billing period");
        }

        BillingPeriodId = billingPeriodId;
        Version++;
    }
}