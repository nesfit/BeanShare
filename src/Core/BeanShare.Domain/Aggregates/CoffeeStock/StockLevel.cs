using System.Security.Cryptography;
using System.Text;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Aggregates.CoffeeStock;

/// <summary>
/// A read-only per-product rollup computed from that product's bags (lots). It is no longer
/// persisted or mutated directly — it is derived on demand from <see cref="Purchase"/> lots so
/// product-keyed features (low-stock alerts, analytics grouping, the consumption product dropdown,
/// QR product-matching) keep a stable per-product view over the per-bag model.
/// </summary>
public sealed class StockLevel
{
    public Guid Id { get; }
    public CoffeeProduct Product { get; }
    public Weight TotalPurchased { get; }
    public Weight TotalConsumed { get; }
    /// <summary>Grams thrown away unbilled (stale/spilled bags discarded by an admin) — gone, but not drunk.</summary>
    public Weight TotalDiscarded { get; }
    public DateTime UpdatedAt { get; }

    // Whether any bag of this product is currently open (being poured from).
    public bool IsOpen { get; }

    private StockLevel(
        Guid id,
        CoffeeProduct product,
        Weight totalPurchased,
        Weight totalConsumed,
        Weight totalDiscarded,
        bool isOpen,
        DateTime updatedAt)
    {
        Id = id;
        Product = product;
        TotalPurchased = totalPurchased;
        TotalConsumed = totalConsumed;
        TotalDiscarded = totalDiscarded;
        IsOpen = isOpen;
        UpdatedAt = updatedAt;
    }

    /// <summary>Build the rollup for a product from all of its bags.</summary>
    public static StockLevel FromLots(CoffeeProduct product, IReadOnlyCollection<Purchase> lots)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(lots);

        var totalPurchased = Weight.FromGrams(lots.Sum(l => l.Quantity.Grams));
        var totalConsumed = Weight.FromGrams(lots.Sum(l => l.ConsumedGrams));
        var totalDiscarded = Weight.FromGrams(lots.Sum(l => l.DiscardedGrams));
        var isOpen = lots.Any(l => l.IsOpen);
        var updatedAt = lots
            .Select(l => l.DepletedAt ?? l.OpenedAt ?? l.CreatedAt)
            .DefaultIfEmpty(default)
            .Max();

        return new StockLevel(DeriveId(product), product, totalPurchased, totalConsumed, totalDiscarded, isOpen, updatedAt);
    }

    public Weight CurrentStock => TotalPurchased.Subtract(TotalConsumed).Subtract(TotalDiscarded);

    public bool IsLowStock(Weight threshold) => CurrentStock <= threshold;

    public decimal ConsumptionPercentage =>
        TotalPurchased.IsZero ? 0 : (TotalConsumed.Grams / TotalPurchased.Grams) * 100;

    // Back-compat aliases so the existing DTO/mapping and QR code keep working over the per-bag model.
    public bool IsCurrentlyUsed => IsOpen;
    public bool IsArchived => false;

    /// <summary>Deterministic id from the product tuple so the projection has a stable key.</summary>
    private static Guid DeriveId(CoffeeProduct product)
    {
        var name = product.Name.ToLowerInvariant();
        var brand = product.Brand.ToLowerInvariant();
        // Length-prefixed so the key is injective over (Name, Brand, Type) even when a field value
        // itself contains the delimiter (e.g. a product named "House|Blend").
        var key = $"{name.Length}:{name}:{brand.Length}:{brand}:{(int)product.Type}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }

    public override string ToString() => $"{Product}: {CurrentStock} available ({ConsumptionPercentage:F1}% consumed)";
}
