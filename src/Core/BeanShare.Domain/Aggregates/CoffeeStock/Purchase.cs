using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Aggregates.CoffeeStock;

/// <summary>
/// A single physical bag of coffee. Historically a "purchase receipt"; it is now also the
/// tracked unit of stock — it carries how much has been drawn from <em>this</em> bag, its
/// lifecycle (<see cref="LotStatus"/>), and the price used to stamp the cost of each cup poured
/// from it. Consumption is allocated to one bag at a time (no cross-lot spill).
/// </summary>
public sealed class Purchase : Entity
{
    public Guid Id { get; private init; }
    public CoffeeProduct Product { get; private set; }
    public Weight Quantity { get; private set; }
    public Money Cost { get; private set; }
    public string Vendor { get; private init; }
    public UserId PurchasedBy { get; private init; }

    /// <summary>Who actually paid for the bag when it was not the admin who recorded it.</summary>
    public UserId? PaidByUserId { get; private set; }
    public DateTime PurchasedAt { get; private set; }
    public DateTime CreatedAt { get; private init; }

    /// <summary>Concurrency token, bumped by every change to the bag's counters, price or payer.</summary>
    public int Version { get; private set; }

    // --- Lot (bag) tracking ---
    public string Label { get; private set; }
    public decimal ConsumedGrams { get; private set; }
    public LotStatus Status { get; private set; }
    public DateTime? OpenedAt { get; private set; }
    public DateTime? DepletedAt { get; private set; }

    /// <summary>
    /// Grams of the latest emptying that were <em>written off</em> rather than poured — the
    /// phantom-fill remainder billed to drinkers when an admin marked the bag empty. Zero for a bag
    /// that ran out cup by cup. Reset by <see cref="UndoWriteOff"/>.
    /// </summary>
    public decimal WrittenOffGrams { get; private set; }

    /// <summary>
    /// Grams of the latest emptying that were <em>discarded</em>: coffee that is gone (stale, spilled,
    /// never really there) and billed to nobody — the space simply eats the cost. Unlike
    /// <see cref="WrittenOffGrams"/> these never became cups. Reset by <see cref="UndoWriteOff"/>.
    /// </summary>
    public decimal DiscardedGrams { get; private set; }

    /// <summary>
    /// Grams of phantom write-off rows an admin voided (DS-42, J1): gone like a discard, billed to nobody, and
    /// never brought back by <see cref="UndoWriteOff"/>.
    /// </summary>
    public decimal VoidedGrams { get; private set; }

    /// <summary>
    /// "Paid in Excel" (DS-41): the bag was settled in the old spreadsheet, so it earns no BeanShare credit and
    /// is left out of the fallback price.
    /// </summary>
    public bool ExcludedFromLedgerCredit { get; private set; }

    /// <summary>How long after an admin empties a bag the write-off can still be undone.</summary>
    public static readonly TimeSpan WriteOffUndoWindow = TimeSpan.FromHours(24);

    /// <summary>Optional scanned EAN/UPC barcode of the bag, so repeat purchases can be prefilled.</summary>
    public string? Barcode { get; private init; }

    /// <summary>The member credited when cups are drawn from this bag.</summary>
    public UserId EffectivePayer => PaidByUserId ?? PurchasedBy;

    private Purchase()
    {
        Product = null!;
        Quantity = null!;
        Cost = null!;
        Vendor = string.Empty;
        PurchasedBy = default;
        Label = string.Empty;
        Status = LotStatus.Sealed;
    }

    private Purchase(
        Guid id,
        CoffeeProduct product,
        Weight quantity,
        Money cost,
        string vendor,
        UserId purchasedBy,
        DateTime purchasedAt,
        DateTime createdAt,
        string label,
        string? barcode)
    {
        Id = id;
        Product = product;
        Quantity = quantity;
        Cost = cost;
        Vendor = vendor;
        PurchasedBy = purchasedBy;
        PurchasedAt = purchasedAt;
        CreatedAt = createdAt;
        Label = label;
        Barcode = barcode;
        ConsumedGrams = 0;
        Status = LotStatus.Sealed;
    }

    public static Purchase Create(
        CoffeeProduct product,
        Weight quantity,
        Money cost,
        string vendor,
        UserId purchasedBy,
        DateTime purchasedAt,
        IClock clock,
        string label = "",
        string? barcode = null,
        UserId? paidBy = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(purchasedBy);

        if (string.IsNullOrWhiteSpace(vendor))
            throw new ArgumentException("Vendor is required", nameof(vendor));

        if (vendor.Length > 100)
            throw new ArgumentException("Vendor name cannot exceed 100 characters", nameof(vendor));

        if (!quantity.IsPositive)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        if (cost.Amount <= 0)
            throw new ArgumentException("Cost must be positive", nameof(cost));

        if (purchasedAt > clock.UtcNow)
            throw new ArgumentException("Purchase date cannot be in the future", nameof(purchasedAt));

        var normalizedBarcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();

        return new Purchase(
            Guid.NewGuid(),
            product,
            quantity,
            cost,
            vendor.Trim(),
            purchasedBy,
            purchasedAt,
            clock.UtcNow,
            label?.Trim() ?? string.Empty,
            normalizedBarcode)
        {
            PaidByUserId = paidBy
        };
    }

    /// <summary>Grams still available in this bag; voided grams (DS-42) are gone for good.</summary>
    public Weight RemainingGrams => Weight.FromGrams(Quantity.Grams - ConsumedGrams - DiscardedGrams - VoidedGrams);

    /// <summary>Full-precision price per gram for this bag (round only the final cup cost).</summary>
    public decimal UnitCostPerGram => Quantity.Grams > 0 ? Cost.Amount / Quantity.Grams : 0m;

    public bool IsOpen => Status == LotStatus.Open;
    public bool IsEmpty => Status == LotStatus.Empty;

    public void Update(Weight newQuantity, decimal newCostAmount, DateTime newPurchasedAt, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newQuantity);

        if (!newQuantity.IsPositive)
            throw new ArgumentException("Quantity must be positive", nameof(newQuantity));

        var used = ConsumedGrams + DiscardedGrams + VoidedGrams;
        if (newQuantity.Grams < used)
            throw new StockDomainException(
                $"Cannot set bag size to {newQuantity.Grams}g — {used}g has already been consumed from or discarded off it.");

        if (newCostAmount <= 0)
            throw new ArgumentException("Cost must be positive", nameof(newCostAmount));

        if (newPurchasedAt > clock.UtcNow)
            throw new ArgumentException("Purchase date cannot be in the future", nameof(newPurchasedAt));

        Quantity = newQuantity;
        Cost = Money.Create(newCostAmount, Cost.Currency);
        PurchasedAt = newPurchasedAt;
        Version++;

        // A resize may re-open a previously depleted bag.
        if (Status == LotStatus.Empty && RemainingGrams.IsPositive)
        {
            // Any Empty bag has been drawn from or written off (ConsumedGrams > 0), so it revives
            // as Open (a partially-used bag), never back to Sealed.
            Status = LotStatus.Open;
            OpenedAt ??= clock.UtcNow;
            DepletedAt = null;
        }
    }

    /// <summary>Marks the bag "Paid in Excel" (DS-41) or clears it; a change bumps <see cref="Version"/>.</summary>
    public void SetExcludedFromLedgerCredit(bool excluded)
    {
        if (ExcludedFromLedgerCredit == excluded)
            return;

        ExcludedFromLedgerCredit = excluded;
        Version++;
    }

    /// <summary>Sets who paid for the bag (null = the recorder). Affects only future settlements.</summary>
    public void SetPaidBy(UserId? paidBy)
    {
        if (PaidByUserId == paidBy)
            return;

        PaidByUserId = paidBy;
        Version++;
    }

    public void UpdateProduct(CoffeeProduct newProduct)
    {
        ArgumentNullException.ThrowIfNull(newProduct);
        Product = newProduct;
    }

    public void SetLabel(string label)
    {
        Label = label?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Mark this bag as the one currently being poured from. Refreshes <see cref="OpenedAt"/> so that,
    /// if more than one bag is open, this becomes the most-recently-opened (the one cups draw from).
    /// </summary>
    public void Open(IClock clock)
    {
        if (Status == LotStatus.Empty)
            throw new StockDomainException($"Bag '{Label}' is empty and cannot be opened.");

        Status = LotStatus.Open;
        OpenedAt = clock.UtcNow;
    }

    /// <summary>
    /// Draw <paramref name="grams"/> from this bag and return the cost of that draw, stamped from
    /// this bag's price. Throws if the bag cannot cover the amount (cups never span two bags).
    /// </summary>
    public Money Consume(Weight grams, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(grams);

        if (!grams.IsPositive)
            throw new ArgumentException("Quantity must be positive", nameof(grams));

        if (Status == LotStatus.Empty || grams.Grams > RemainingGrams.Grams)
            throw new InsufficientStockException(Product.Name, Product.Brand, grams.Grams, RemainingGrams.Grams);

        if (Status == LotStatus.Sealed)
        {
            Status = LotStatus.Open;
            OpenedAt ??= clock.UtcNow;
        }

        ConsumedGrams += grams.Grams;
        Version++;
        var cost = Money.Create(UnitCostPerGram * grams.Grams, Cost.Currency);

        if (RemainingGrams.IsZero)
        {
            Status = LotStatus.Empty;
            DepletedAt = clock.UtcNow;
        }

        return cost;
    }

    /// <summary>
    /// Write off <paramref name="grams"/> of this bag: drawn like <see cref="Consume"/> (so the
    /// remainder is billed at this bag's €/g) but remembered as a write-off, so an accidental
    /// "mark empty" can be reversed with <see cref="UndoWriteOff"/>.
    /// </summary>
    public Money WriteOff(Weight grams, IClock clock)
    {
        var cost = Consume(grams, clock);
        WrittenOffGrams += grams.Grams;
        return cost;
    }

    /// <summary>
    /// Discard whatever is left in this bag without billing anyone: the coffee is gone (stale, spilled,
    /// or never really there) and the space eats the cost. The bag becomes Empty; undoable like a
    /// write-off via <see cref="UndoWriteOff"/>. Returns the grams discarded.
    /// </summary>
    public Weight Discard(IClock clock)
    {
        if (Status == LotStatus.Empty || !RemainingGrams.IsPositive)
            throw new StockDomainException($"Bag '{Label}' is already empty; nothing to discard.");

        var discarded = RemainingGrams;
        DiscardedGrams += discarded.Grams;
        Version++;
        Status = LotStatus.Empty;
        DepletedAt = clock.UtcNow;
        return discarded;
    }

    /// <summary>Grams of the latest emptying that did not come from cups: billed write-off plus unbilled discard.</summary>
    public decimal EmptiedByAdminGrams => WrittenOffGrams + DiscardedGrams;

    /// <summary>Moment after which the latest write-off/discard can no longer be undone; null when there is nothing to undo.</summary>
    public DateTime? WriteOffUndoDeadline =>
        Status == LotStatus.Empty && EmptiedByAdminGrams > 0 && DepletedAt.HasValue
            ? DepletedAt.Value + WriteOffUndoWindow
            : null;

    /// <summary>True while the latest write-off can still be undone (bag is empty, something was written off, window not passed).</summary>
    public bool IsWriteOffUndoable(DateTime now) => WriteOffUndoDeadline is { } deadline && now <= deadline;

    /// <summary>
    /// Reverse the latest admin emptying: written-off and discarded grams go back into the bag, which
    /// returns to <see cref="LotStatus.Sealed"/> if nothing had ever been poured from it, else to
    /// <see cref="LotStatus.Open"/>. Returns the grams restored.
    /// </summary>
    public Weight UndoWriteOff(IClock clock)
    {
        if (Status != LotStatus.Empty || EmptiedByAdminGrams <= 0)
            throw new StockDomainException($"Bag '{Label}' has no write-off or discard to undo.");

        var restored = Math.Min(WrittenOffGrams, ConsumedGrams) + DiscardedGrams;
        ConsumedGrams -= Math.Min(WrittenOffGrams, ConsumedGrams);
        WrittenOffGrams = 0;
        DiscardedGrams = 0;
        DepletedAt = null;
        Version++;

        if (ConsumedGrams == 0)
        {
            Status = LotStatus.Sealed;
            OpenedAt = null;
        }
        else
        {
            Status = LotStatus.Open;
            OpenedAt ??= clock.UtcNow;
        }

        return Weight.FromGrams(restored);
    }

    /// <summary>
    /// Reverse a prior draw (used when a consumption is edited or deleted). A deleted write-off share
    /// (<paramref name="writeOff"/>) also leaves the write-off counter (Z2, B25), so a later "Undo empty"
    /// restores only what is still written off; a normal cup leaves that counter alone.
    /// </summary>
    public void Restore(Weight grams, IClock clock, bool writeOff = false)
    {
        ArgumentNullException.ThrowIfNull(grams);

        if (!grams.IsPositive)
            return;

        var restored = Math.Min(grams.Grams, ConsumedGrams);
        ConsumedGrams -= restored;
        Version++;
        if (writeOff)
            WrittenOffGrams = Math.Max(0m, WrittenOffGrams - restored);
        WrittenOffGrams = Math.Min(WrittenOffGrams, ConsumedGrams);

        if (Status == LotStatus.Empty && RemainingGrams.IsPositive)
        {
            // Any Empty bag has been drawn from or written off (ConsumedGrams > 0), so it revives
            // as Open (a partially-used bag), never back to Sealed.
            Status = LotStatus.Open;
            OpenedAt ??= clock.UtcNow;
            DepletedAt = null;
        }
    }

    /// <summary>
    /// Voids <paramref name="grams"/> of phantom write-off shares (DS-42, J1): they leave the consumed and
    /// written-off counters for <see cref="VoidedGrams"/>, so <see cref="RemainingGrams"/> and
    /// <see cref="Status"/> stay as they are, nothing returns to stock and <see cref="UndoWriteOff"/> never
    /// brings them back. At most the consumed grams are moved (J2). Returns the grams voided.
    /// </summary>
    public Weight VoidWriteOff(Weight grams)
    {
        ArgumentNullException.ThrowIfNull(grams);

        var voided = Math.Min(Math.Max(0m, grams.Grams), ConsumedGrams);
        if (voided == 0)
            return Weight.Zero;

        ConsumedGrams -= voided;
        WrittenOffGrams = Math.Min(Math.Max(0m, WrittenOffGrams - voided), ConsumedGrams);
        VoidedGrams += voided;
        Version++;
        return Weight.FromGrams(voided);
    }

    public Money CostPerGram => Cost.Divide(Quantity.Grams);

    protected override object GetId() => Id;

    public override string ToString() =>
        $"{Product} {Label} - {RemainingGrams}/{Quantity} left ({Status}) for {Cost} from {Vendor}";
}
