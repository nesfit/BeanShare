namespace BeanShare.Application.Features.ActiveQrCodes.Dtos;

public sealed record ActiveQrCodeDto(
    Guid Id,
    Guid SpaceId,
    string Label,
    string ProductName,
    string ProductBrand,
    string ProductType,
    string ProductDisplayName,
    string RecipeName,
    int DefaultGrams,
    DateTime CreatedAt,
    bool IsActive,
    Guid? StockLotId = null);
