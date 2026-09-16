using BeanShare.Domain.Common;
using BeanShare.Domain.Events;
using BeanShare.Domain.ValueObjects;
using BeanShare.Domain.Exceptions;

namespace BeanShare.Domain.Aggregates.CoffeeStock;

public sealed class CoffeeStock : AggregateRoot
{
    private readonly List<Purchase> _purchases = [];

    public CoffeeStockId Id { get; private set; }
    public SpaceId SpaceId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Every bag (lot) ever purchased for this space — the source of truth for stock.</summary>
    public IReadOnlyCollection<Purchase> Purchases => _purchases.AsReadOnly();

    /// <summary>Per-product rollups, derived on demand from the bags (no longer persisted).</summary>
    public IReadOnlyCollection<StockLevel> StockLevels =>
        _purchases
            .GroupBy(p => ProductKey(p.Product))
            .Select(g => StockLevel.FromLots(g.First().Product, g.ToList()))
            .ToList();

    private CoffeeStock()
    {
        Id = null!;
        SpaceId = default;
    }

    private CoffeeStock(CoffeeStockId id, SpaceId spaceId, DateTime createdAt)
    {
        Id = id;
        SpaceId = spaceId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static CoffeeStock Create(SpaceId spaceId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(spaceId);

        var id = CoffeeStockId.New();
        var createdAt = clock.UtcNow;

        var stock = new CoffeeStock(id, spaceId, createdAt);

        stock.RaiseDomainEvent(new CoffeeStockCreated(id, spaceId, createdAt));

        return stock;
    }

    /// <summary>
    /// Record a purchase of <paramref name="bagCount"/> identical bags, each of
    /// <paramref name="quantity"/> grams at <paramref name="cost"/>. Each bag becomes its own
    /// tracked lot (labelled "#N" within the product), so "2×1000 g" is two separate bags.
    /// </summary>
    public void AddPurchase(
        CoffeeProduct product,
        Weight quantity,
        Money cost,
        string vendor,
        UserId purchasedBy,
        DateTime purchasedAt,
        IClock clock,
        int bagCount = 1,
        string? barcode = null)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (bagCount < 1)
            throw new ArgumentException("Bag count must be at least 1", nameof(bagCount));

        var baseIndex = MaxLabelIndex(product);

        for (var i = 0; i < bagCount; i++)
        {
            var label = $"#{baseIndex + i + 1}";

            // Each lot must own its OWN value-object instances: Product/Quantity/Cost are EF owned
            // types, and one instance cannot be shared across multiple owner rows (the extra rows
            // would persist with null owned columns). Clone per lot.
            var lotProduct = CoffeeProduct.Create(product.Name, product.Brand, product.Type);
            var lotQuantity = Weight.FromGrams(quantity.Grams);
            var lotCost = Money.Create(cost.Amount, cost.Currency);

            var lot = Purchase.Create(lotProduct, lotQuantity, lotCost, vendor, purchasedBy, purchasedAt, clock, label, barcode);
            _purchases.Add(lot);

            RaiseDomainEvent(new StockPurchaseAdded(
                Id, SpaceId, lot.Id, lotProduct, lotQuantity, lotCost, vendor, purchasedBy, purchasedAt, clock.UtcNow));
        }

        UpdatedAt = clock.UtcNow;
    }

    public void UpdatePurchase(Guid purchaseId, Weight newQuantity, decimal newCostAmount, DateTime newPurchasedAt, IClock clock,
        string? newProductName = null, string? newProductBrand = null, CoffeeType? newCoffeeType = null)
    {
        var purchase = _purchases.FirstOrDefault(p => p.Id == purchaseId);
        if (purchase == null)
            throw new InvalidOperationException($"Purchase {purchaseId} not found");

        purchase.Update(newQuantity, newCostAmount, newPurchasedAt, clock);

        if (IsProductIdentityChanged(purchase.Product, newProductName, newProductBrand, newCoffeeType))
        {
            var updatedProduct = CoffeeProduct.Create(
                newProductName ?? purchase.Product.Name,
                newProductBrand ?? purchase.Product.Brand,
                newCoffeeType ?? purchase.Product.Type);

            purchase.UpdateProduct(updatedProduct);
            purchase.SetLabel($"#{MaxLabelIndex(updatedProduct, purchaseId) + 1}");
        }

        UpdatedAt = clock.UtcNow;
    }

    public void DeletePurchase(Guid purchaseId, IClock clock)
    {
        var purchase = _purchases.FirstOrDefault(p => p.Id == purchaseId);
        if (purchase == null)
            throw new InvalidOperationException($"Purchase {purchaseId} not found");

        if (purchase.ConsumedGrams > 0)
            throw new StockDomainException(
                $"Cannot delete bag '{purchase.Label}' — {purchase.ConsumedGrams}g has already been consumed from it. Empty it (bill the drinkers) or discard it (bill nobody) instead.");

        _purchases.Remove(purchase);
        UpdatedAt = clock.UtcNow;
    }

    /// <summary>
    /// Consume <paramref name="quantity"/> of a product from a single bag and return the allocation
    /// (which bag, the cost stamped from its price, and the product's remaining stock). The bag is
    /// chosen as: the bound <paramref name="targetLotId"/> if it is a non-empty bag of this product;
    /// otherwise the most-recently-opened bag; otherwise the oldest non-empty bag (auto-opened).
    /// Throws <see cref="InsufficientStockException"/> if the chosen bag cannot cover the amount —
    /// cups never span two bags.
    /// </summary>
    public StockAllocation ConsumeStock(CoffeeProduct product, Weight quantity, IClock clock, Guid? targetLotId = null)
    {
        var lots = _purchases.Where(p => SameProduct(p.Product, product)).ToList();
        if (lots.Count == 0)
            throw new ProductNotFoundException(product.Name, product.Brand);

        Purchase? target = null;
        if (targetLotId.HasValue)
            target = lots.FirstOrDefault(l => l.Id == targetLotId.Value && !l.IsEmpty);

        target ??= lots.Where(l => l.IsOpen).OrderByDescending(l => l.OpenedAt).FirstOrDefault();
        target ??= lots.Where(l => !l.IsEmpty).OrderBy(l => l.PurchasedAt).ThenBy(l => l.CreatedAt).FirstOrDefault();

        if (target == null)
            throw new InsufficientStockException(product.Name, product.Brand, quantity.Grams, 0);

        var cost = target.Consume(quantity, clock);
        UpdatedAt = clock.UtcNow;

        var productRemaining = Weight.FromGrams(lots.Sum(l => l.RemainingGrams.Grams));

        RaiseDomainEvent(new StockConsumed(Id, SpaceId, product, quantity, productRemaining, clock.UtcNow));

        return new StockAllocation(target.Id, target.Label, quantity, target.UnitCostPerGram, cost, productRemaining);
    }

    /// <summary>Best-effort product-level reversal (restores newest bags first). Prefer <see cref="RestoreStockToLot"/>.</summary>
    public void RestoreStock(CoffeeProduct product, Weight quantity, IClock clock)
    {
        var lots = _purchases
            .Where(p => SameProduct(p.Product, product))
            .OrderByDescending(l => l.PurchasedAt).ThenByDescending(l => l.CreatedAt)
            .ToList();

        if (lots.Count == 0)
            throw new ProductNotFoundException(product.Name, product.Brand);

        var remaining = quantity.Grams;
        foreach (var lot in lots)
        {
            if (remaining <= 0)
                break;

            var restorable = Math.Min(remaining, lot.ConsumedGrams);
            if (restorable > 0)
            {
                lot.Restore(Weight.FromGrams(restorable), clock);
                remaining -= restorable;
            }
        }

        UpdatedAt = clock.UtcNow;
    }

    /// <summary>Reverse a draw against a specific bag (used to undo a consumption edit/delete).</summary>
    public void RestoreStockToLot(Guid lotId, Weight quantity, IClock clock)
    {
        var lot = _purchases.FirstOrDefault(p => p.Id == lotId)
            ?? throw new StockDomainException($"Bag {lotId} not found");

        lot.Restore(quantity, clock);
        UpdatedAt = clock.UtcNow;
    }

    /// <summary>
    /// Write off <paramref name="quantity"/> of a specific bag (the phantom-fill remainder when an
    /// admin marks it empty). Same allocation/cost stamping as <see cref="ConsumeStock"/>, but the
    /// bag remembers the written-off grams so the emptying can be undone.
    /// </summary>
    public StockAllocation WriteOffStock(Guid lotId, Weight quantity, IClock clock)
    {
        var lot = _purchases.FirstOrDefault(p => p.Id == lotId)
            ?? throw new StockDomainException($"Bag {lotId} not found");

        var cost = lot.WriteOff(quantity, clock);
        UpdatedAt = clock.UtcNow;

        var productRemaining = Weight.FromGrams(
            _purchases.Where(p => SameProduct(p.Product, lot.Product)).Sum(l => l.RemainingGrams.Grams));

        RaiseDomainEvent(new StockConsumed(Id, SpaceId, lot.Product, quantity, productRemaining, clock.UtcNow));

        return new StockAllocation(lot.Id, lot.Label, quantity, lot.UnitCostPerGram, cost, productRemaining);
    }

    /// <summary>
    /// Discard whatever is left in a bag without billing anyone (see <see cref="Purchase.Discard"/>).
    /// Returns the grams discarded.
    /// </summary>
    public Weight DiscardBag(Guid lotId, IClock clock)
    {
        var lot = _purchases.FirstOrDefault(p => p.Id == lotId)
            ?? throw new StockDomainException($"Bag {lotId} not found");

        var discarded = lot.Discard(clock);
        UpdatedAt = clock.UtcNow;
        return discarded;
    }

    /// <summary>Reverse the latest write-off/discard of a bag (see <see cref="Purchase.UndoWriteOff"/>). Returns the grams restored.</summary>
    public Weight UndoBagWriteOff(Guid lotId, IClock clock)
    {
        var lot = _purchases.FirstOrDefault(p => p.Id == lotId)
            ?? throw new StockDomainException($"Bag {lotId} not found");

        var restored = lot.UndoWriteOff(clock);
        UpdatedAt = clock.UtcNow;
        return restored;
    }

    /// <summary>Mark a bag as the one currently being poured from.</summary>
    public void OpenBag(Guid lotId, IClock clock)
    {
        var lot = _purchases.FirstOrDefault(p => p.Id == lotId)
            ?? throw new StockDomainException($"Bag {lotId} not found");

        lot.Open(clock);
        UpdatedAt = clock.UtcNow;
    }

    public Weight GetCurrentStock(CoffeeProduct product) =>
        Weight.FromGrams(_purchases.Where(p => SameProduct(p.Product, product)).Sum(p => p.RemainingGrams.Grams));

    public IEnumerable<StockLevel> GetLowStockProducts(Weight threshold) =>
        StockLevels.Where(sl => sl.IsLowStock(threshold));

    public Money GetTotalInvestment(string currency = "USD")
    {
        var totalAmount = _purchases
            .Where(p => p.Cost.Currency == currency)
            .Sum(p => p.Cost.Amount);

        return Money.Create(totalAmount, currency);
    }

    public bool HasStock(CoffeeProduct product) => GetCurrentStock(product).IsPositive;

    /// <summary>Distinct products that still have stock in at least one bag.</summary>
    public int ProductVarietyCount =>
        _purchases.Where(p => !p.IsEmpty).Select(p => ProductKey(p.Product)).Distinct().Count();

    public Weight TotalCurrentStock =>
        Weight.FromGrams(_purchases.Sum(p => p.RemainingGrams.Grams));

    private static int ParseLabelIndex(string label) =>
        label.StartsWith('#') && int.TryParse(label.AsSpan(1), out var n) ? n : 0;

    // Next label is max(existing "#N") + 1 — not a count — so labels are not reused after a bag is
    // deleted or moved to another product.
    private int MaxLabelIndex(CoffeeProduct product, Guid? excludeId = null) =>
        _purchases
            .Where(p => (excludeId == null || p.Id != excludeId.Value) && SameProduct(p.Product, product))
            .Select(p => ParseLabelIndex(p.Label))
            .DefaultIfEmpty(0)
            .Max();

    private static (string, string, CoffeeType) ProductKey(CoffeeProduct p) =>
        (p.Name.ToLowerInvariant(), p.Brand.ToLowerInvariant(), p.Type);

    private static bool SameProduct(CoffeeProduct a, CoffeeProduct b) =>
        string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Brand, b.Brand, StringComparison.OrdinalIgnoreCase) &&
        a.Type == b.Type;

    private static bool IsProductIdentityChanged(
        CoffeeProduct oldProduct, string? newProductName, string? newProductBrand, CoffeeType? newCoffeeType)
    {
        return (newProductName != null && !string.Equals(newProductName, oldProduct.Name, StringComparison.OrdinalIgnoreCase))
            || (newProductBrand != null && !string.Equals(newProductBrand, oldProduct.Brand, StringComparison.OrdinalIgnoreCase))
            || (newCoffeeType.HasValue && newCoffeeType.Value != oldProduct.Type);
    }
}
