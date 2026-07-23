using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.ActiveQrCodes.Dtos;

namespace BeanShare.Application.Features.ActiveQrCodes.Commands;

public sealed record RenameActiveQrCodeCommand(
    Guid QrCodeId,
    string Label
) : ICommand<Result<ActiveQrCodeDto>>;
