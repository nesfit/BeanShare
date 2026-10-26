using BeanShare.Domain.Aggregates.CoffeeStock;

namespace BeanShare.Application.Features.CoffeeStock;

/// <summary>How messages name a bag: product and label, or product and purchase date without a label.</summary>
internal static class BagNames
{
    public static string Of(Purchase bag) =>
        string.IsNullOrWhiteSpace(bag.Label)
            ? $"{bag.Product.DisplayName} ({bag.PurchasedAt:yyyy-MM-dd})"
            : $"{bag.Product.DisplayName} {bag.Label}";
}
