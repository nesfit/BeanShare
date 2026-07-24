using BeanShare.Application.Features.ActiveQrCodes.Dtos;

namespace BeanShare.Maui.Services;

public interface IActiveQrService
{
    Task<ActiveQrCodeDto?> CreateActiveQrCodeAsync(Guid spaceId, string label, string productName, string productBrand, string productType, string recipeName, int defaultGrams, Guid? stockLotId = null);
    Task<List<ActiveQrCodeDto>?> GetActiveQrCodesAsync(Guid spaceId);
    Task<ActiveQrCodeDto?> ResolveActiveQrCodeAsync(Guid qrCodeId);
    Task<ActiveQrCodeDto?> ReassignActiveQrCodeAsync(Guid qrCodeId, string productName, string productBrand, string productType, string recipeName, int defaultGrams, Guid? stockLotId = null);
    Task<ActiveQrCodeDto?> RenameActiveQrCodeAsync(Guid qrCodeId, string label);
    Task<bool> DeactivateActiveQrCodeAsync(Guid qrCodeId);
}
