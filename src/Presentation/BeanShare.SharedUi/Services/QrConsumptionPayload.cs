using System.Text.Json.Serialization;

namespace BeanShare.SharedUi.Services;

public sealed class QrConsumptionPayload
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("s")]
    public Guid SpaceId { get; set; }

    [JsonPropertyName("n")]
    public string ProductName { get; set; } = "";

    [JsonPropertyName("b")]
    public string ProductBrand { get; set; } = "";

    [JsonPropertyName("t")]
    public string ProductType { get; set; } = "";

    [JsonPropertyName("g")]
    public int DefaultGrams { get; set; }

    [JsonPropertyName("q")]
    public Guid? QrCodeId { get; set; }

    [JsonIgnore]
    public string? QrLabel { get; set; }

    [JsonIgnore]
    public string? RecipeName { get; set; }

    /// <summary>
    /// Bag (Purchase lot) this QR is bound to, resolved server-side. Not encoded in the QR image;
    /// filled from the resolved <c>ActiveQrCode</c>. When set, the scanned cup is logged against
    /// this bag (falling back to the product's open bag if it is empty/gone).
    /// </summary>
    [JsonIgnore]
    public Guid? StockLotId { get; set; }
}
