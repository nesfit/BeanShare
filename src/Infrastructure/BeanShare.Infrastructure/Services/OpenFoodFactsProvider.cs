using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BeanShare.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace BeanShare.Infrastructure.Services;

/// <summary>
/// Resolves a barcode to product metadata via the public Open Food Facts database.
/// Best-effort: returns <c>null</c> (never throws) when the product is unknown or the API is down.
/// </summary>
public sealed partial class OpenFoodFactsProvider : IProductCatalogService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenFoodFactsProvider> _logger;

    public OpenFoodFactsProvider(HttpClient httpClient, ILogger<OpenFoodFactsProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ProductCatalogResult?> LookupAsync(string barcode, CancellationToken cancellationToken = default)
    {
        var code = new string((barcode ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        try
        {
            var response = await _httpClient.GetFromJsonAsync<OffResponse>(
                $"api/v2/product/{code}.json?fields=product_name,brands,quantity",
                cancellationToken);

            if (response is null || response.Status != 1 || response.Product is null)
            {
                return null;
            }

            var name = Clean(response.Product.ProductName);
            var brand = Clean(response.Product.Brands?.Split(',').FirstOrDefault());
            var grams = ParseGrams(response.Product.Quantity);

            if (name is null && brand is null && grams is null)
            {
                return null;
            }

            return new ProductCatalogResult(name, brand, grams);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Open Food Facts lookup failed for barcode {Barcode}", code);
            return null;
        }
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    /// <summary>Parse a net-weight string like "1kg", "250 g", "1 kg", "500g" into grams.</summary>
    private static int? ParseGrams(string? quantity)
    {
        if (string.IsNullOrWhiteSpace(quantity))
        {
            return null;
        }

        var match = WeightRegex().Match(quantity);
        if (!match.Success)
        {
            return null;
        }

        if (!decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var unit = match.Groups[2].Value.ToLowerInvariant();
        var grams = unit switch
        {
            "kg" => value * 1000m,
            _ => value, // g / gram / grams
        };

        var rounded = (int)Math.Round(grams);
        return rounded is > 0 and <= 100000 ? rounded : null;
    }

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(kg|g|gram|grams)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeightRegex();

    private sealed record OffResponse(
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("product")] OffProduct? Product);

    private sealed record OffProduct(
        [property: JsonPropertyName("product_name")] string? ProductName,
        [property: JsonPropertyName("brands")] string? Brands,
        [property: JsonPropertyName("quantity")] string? Quantity);
}
