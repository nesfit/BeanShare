using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Entities;

public sealed class ActiveQrCode : Entity
{
    public const int MaxLabelLength = 100;

    public ActiveQrCodeId Id { get; private set; }
    public SpaceId SpaceId { get; private set; }
    public string Label { get; private set; }
    public CoffeeProduct Product { get; private set; }
    public string RecipeName { get; private set; }
    public int DefaultGrams { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// Optionally binds this QR to a specific bag (Purchase lot). When set, scanning logs a cup
    /// against that bag; when null (or the bag is empty/gone) it falls back to the product's open bag.
    /// </summary>
    public Guid? StockLotId { get; private set; }

    private ActiveQrCode()
    {
        Id = default!;
        SpaceId = default;
        Label = string.Empty;
        Product = CoffeeProduct.Create("Unknown", "Unknown", CoffeeType.Espresso);
        RecipeName = string.Empty;
    }

    private ActiveQrCode(
        ActiveQrCodeId id,
        SpaceId spaceId,
        string label,
        CoffeeProduct product,
        string recipeName,
        int defaultGrams,
        DateTime createdAt,
        Guid? stockLotId)
    {
        Id = id;
        SpaceId = spaceId;
        Label = label;
        Product = product;
        RecipeName = recipeName;
        DefaultGrams = defaultGrams;
        CreatedAt = createdAt;
        IsActive = true;
        StockLotId = stockLotId;
    }

    public bool HasOpenRecipe => DefaultGrams == 0;

    public static ActiveQrCode Create(
        SpaceId spaceId,
        string label,
        CoffeeProduct product,
        string recipeName,
        int defaultGrams,
        IClock clock,
        Guid? stockLotId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(clock);

        if (label.Trim().Length > MaxLabelLength)
        {
            throw new ArgumentException($"Label cannot exceed {MaxLabelLength} characters", nameof(label));
        }

        if (defaultGrams < 0)
        {
            throw new ArgumentException("Default grams cannot be negative", nameof(defaultGrams));
        }

        if (defaultGrams > 0)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(recipeName);
        }

        return new ActiveQrCode(
            ActiveQrCodeId.New(),
            spaceId,
            label.Trim(),
            product,
            recipeName?.Trim() ?? string.Empty,
            defaultGrams,
            clock.UtcNow,
            stockLotId);
    }

    public void Reassign(CoffeeProduct product, string recipeName, int defaultGrams)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (defaultGrams < 0)
        {
            throw new ArgumentException("Default grams cannot be negative", nameof(defaultGrams));
        }

        if (defaultGrams > 0)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(recipeName);
        }

        Product = product;
        RecipeName = recipeName?.Trim() ?? string.Empty;
        DefaultGrams = defaultGrams;
        // Bag binding (StockLotId) is managed explicitly via BindToBag/UnbindBag — reassigning the
        // product/recipe must NOT silently clobber an existing binding.
    }

    /// <summary>Pin this QR to a specific bag (Purchase lot).</summary>
    public void BindToBag(Guid stockLotId) => StockLotId = stockLotId;

    /// <summary>Remove any bag binding; scans fall back to the product's open bag.</summary>
    public void UnbindBag() => StockLotId = null;

    public void UpdateLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var trimmed = label.Trim();
        if (trimmed.Length > MaxLabelLength)
        {
            throw new ArgumentException($"Label cannot exceed {MaxLabelLength} characters", nameof(label));
        }

        Label = trimmed;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    protected override object GetId() => Id;
}
