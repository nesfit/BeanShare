using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.ActiveQrCodes.Dtos;

namespace BeanShare.Application.Features.ActiveQrCodes.Commands;

[RequireSpaceMember("SpaceId")]
public sealed record CreateActiveQrCodeCommand(
    Guid SpaceId,
    string Label,
    string ProductName,
    string ProductBrand,
    string ProductType,
    string RecipeName,
    int DefaultGrams,
    Guid? StockLotId = null
) : IAuthorize, ICommand<Result<ActiveQrCodeDto>>;
