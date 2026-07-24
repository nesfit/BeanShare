namespace BeanShare.SharedUi.Components.Consumption.Models;

public sealed class QuickConsumptionRequest
{
    public Guid SpaceId { get; set; }
    public int Grams { get; set; }
    public string ProductName { get; set; } = "";
    public string ProductBrand { get; set; } = "";
    public string ProductType { get; set; } = "";
    public string? PresetName { get; set; }
    public DateTime ConsumedAt { get; set; } = DateTime.Now;
    public List<Guid>? ForUserIds { get; set; }

    // Optional bag (Purchase lot) to draw from — set by the QR quick-consume flow when the scanned
    // code is bound to a specific bag. Null = the product's open bag.
    public Guid? StockLotId { get; set; }
}
