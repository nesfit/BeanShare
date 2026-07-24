namespace BeanShare.Contracts.ActiveQrCodes;

public sealed record ActiveQrCodeResponse
{
    public required Guid Id { get; init; }
    public required Guid SpaceId { get; init; }
    public required string Label { get; init; }
    public required string ProductName { get; init; }
    public required string ProductBrand { get; init; }
    public required string ProductType { get; init; }
    public required string ProductDisplayName { get; init; }
    public required string RecipeName { get; init; }
    public required int DefaultGrams { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required bool IsActive { get; init; }

    // Bag (Purchase lot) this QR is bound to, if any.
    public Guid? StockLotId { get; init; }
}
