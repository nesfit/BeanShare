namespace BeanShare.Contracts.ActiveQrCodes;

public sealed record ReassignActiveQrCodeRequest
{
    public required string ProductName { get; init; }
    public required string ProductBrand { get; init; }
    public required string ProductType { get; init; }
    public required string RecipeName { get; init; }
    public required int DefaultGrams { get; init; }

    // Optional bag (Purchase lot) to re-bind this QR to. Null clears any binding.
    public Guid? StockLotId { get; init; }
}
