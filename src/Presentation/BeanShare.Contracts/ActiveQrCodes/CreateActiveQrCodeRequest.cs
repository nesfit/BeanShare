namespace BeanShare.Contracts.ActiveQrCodes;

public sealed record CreateActiveQrCodeRequest
{
    public required Guid SpaceId { get; init; }
    public required string Label { get; init; }
    public required string ProductName { get; init; }
    public required string ProductBrand { get; init; }
    public required string ProductType { get; init; }
    public required string RecipeName { get; init; }
    public required int DefaultGrams { get; init; }

    // Optional bag (Purchase lot) to bind this QR to; scans fall back to the open bag if empty/gone.
    public Guid? StockLotId { get; init; }
}
