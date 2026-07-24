using BeanShare.Domain.Entities;

namespace BeanShare.Application.Features.ActiveQrCodes.Dtos;

public static class ActiveQrCodeMapper
{
    public static ActiveQrCodeDto ToDto(ActiveQrCode qrCode) => new(
        Id: qrCode.Id.Value,
        SpaceId: qrCode.SpaceId.Value,
        Label: qrCode.Label,
        ProductName: qrCode.Product.Name,
        ProductBrand: qrCode.Product.Brand,
        ProductType: qrCode.Product.Type.ToString(),
        ProductDisplayName: qrCode.Product.DisplayName,
        RecipeName: qrCode.RecipeName,
        DefaultGrams: qrCode.DefaultGrams,
        CreatedAt: qrCode.CreatedAt,
        IsActive: qrCode.IsActive,
        StockLotId: qrCode.StockLotId);
}
