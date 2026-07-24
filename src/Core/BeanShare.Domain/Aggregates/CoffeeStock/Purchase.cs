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
    public DateTime PurchasedAt { get; private set; }
    public DateTime CreatedAt { get; private init; }

    // --- Lot (bag) tracking ---
    public string Label { get; private set; }
    public decimal ConsumedGrams { get; private set; }
    public LotStatus Status { get; private set; }
    public DateTime? OpenedAt { get; private set; }
    public DateTime? DepletedAt { get; private set; }

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
        string label)
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
        string label = "")
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

        return new Purchase(
            Guid.NewGuid(),
            product,
            quantity,
            cost,
            vendor.Trim(),
            purchasedBy,
            purchasedAt,
            clock.UtcNow,
            label?.Trim() ?? string.Empty);
    }

    /// <summary>Grams still available in this bag.</summary>
    public Weight RemainingGrams => Weight.FromGrams(Quantity.Grams - ConsumedGrams);

    /// <summary>Full-precision price per gram for this bag (round only the final cup cost).</summary>
    public decimal UnitCostPerGram => Quantity.Grams > 0 ? Cost.Amount / Quantity.Grams : 0m;

    public bool IsOpen => Status == LotStatus.Open;
    public bool IsEmpty => Status == LotStatus.Empty;

    public void Update(Weight newQuantity, decimal newCostAmount, DateTime newPurchasedAt, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newQuantity);

        if (!newQuantity.IsPositive)
            throw new ArgumentException("Quantity must be positive", nameof(newQuantity));

        if (newQuantity.Grams < ConsumedGrams)
            throw new StockDomainException(
                $"Cannot set bag size to {newQuantity.Grams}g — {ConsumedGrams}g has already been consumed from it.");

        if (newCostAmount <= 0)
            throw new ArgumentException("Cost must be positive", nameof(newCostAmount));

        if (newPurchasedAt > clock.UtcNow)
            throw new ArgumentException("Purchase date cannot be in the future", nameof(newPurchasedAt));

        Quantity = newQuantity;
        Cost = Money.Create(newCostAmount, Cost.Currency);
        PurchasedAt = newPurchasedAt;

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
        var cost = Money.Create(UnitCostPerGram * grams.Grams, Cost.Currency);

        if (RemainingGrams.IsZero)
        {
            Status = LotStatus.Empty;
            DepletedAt = clock.UtcNow;
        }

        return cost;
    }

    /// <summary>Reverse a prior draw (used when a consumption is edited or deleted).</summary>
    public void Restore(Weight grams, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(grams);

        if (!grams.IsPositive)
            return;

        var restored = Math.Min(grams.Grams, ConsumedGrams);
        ConsumedGrams -= restored;

        if (Status == LotStatus.Empty && RemainingGrams.IsPositive)
        {
            // Any Empty bag has been drawn from or written off (ConsumedGrams > 0), so it revives
            // as Open (a partially-used bag), never back to Sealed.
            Status = LotStatus.Open;
            OpenedAt ??= clock.UtcNow;
            DepletedAt = null;
        }
    }

    public Money CostPerGram => Cost.Divide(Quantity.Grams);

    protected override object GetId() => Id;

    public override string ToString() =>
        $"{Product} {Label} - {RemainingGrams}/{Quantity} left ({Status}) for {Cost} from {Vendor}";
}
