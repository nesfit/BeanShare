using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.ActiveQrCodes.Dtos;

namespace BeanShare.Application.Features.ActiveQrCodes.Commands;

public sealed record ReassignActiveQrCodeCommand(
    Guid QrCodeId,
    string ProductName,
    string ProductBrand,
    string ProductType,
    string RecipeName,
    int DefaultGrams,
    Guid? StockLotId = null
) : ICommand<Result<ActiveQrCodeDto>>;
