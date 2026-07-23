using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.ActiveQrCodes.Dtos;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.ActiveQrCodes.Commands;

public sealed class RenameActiveQrCodeCommandHandler : IRequestHandler<RenameActiveQrCodeCommand, Result<ActiveQrCodeDto>>
{
    private readonly IActiveQrCodeRepository _qrCodeRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;

    public RenameActiveQrCodeCommandHandler(
        IActiveQrCodeRepository qrCodeRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext)
    {
        _qrCodeRepository = qrCodeRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
    }

    public async Task<Result<ActiveQrCodeDto>> Handle(RenameActiveQrCodeCommand command, CancellationToken cancellationToken)
    {
        var qrCodeId = new ActiveQrCodeId(command.QrCodeId);
        var qrCode = await _qrCodeRepository.GetByIdAsync(qrCodeId, cancellationToken);

        if (qrCode is null)
        {
            return Result<ActiveQrCodeDto>.Failure(Error.NotFound("ActiveQrCode", $"QR code {command.QrCodeId} not found"));
        }

        if (!qrCode.IsActive)
        {
            return Result<ActiveQrCodeDto>.Failure(Error.NotFound("ActiveQrCode", $"QR code {command.QrCodeId} is no longer active"));
        }

        var space = await _spaceRepository.GetByIdAsync(qrCode.SpaceId, cancellationToken);

        if (space is null)
        {
            return Result<ActiveQrCodeDto>.Failure(Error.SpaceNotFound(qrCode.SpaceId.Value));
        }

        if (!space.HasMember(_userContext.CurrentUserId))
        {
            return Result<ActiveQrCodeDto>.Failure(Error.NotMember());
        }

        try
        {
            qrCode.UpdateLabel(command.Label);
            await _qrCodeRepository.UpdateAsync(qrCode, cancellationToken);

            return Result<ActiveQrCodeDto>.Success(ActiveQrCodeMapper.ToDto(qrCode));
        }
        catch (ArgumentException ex)
        {
            return Result<ActiveQrCodeDto>.Failure(Error.ValidationFailure(nameof(RenameActiveQrCodeCommand), ex.Message));
        }
    }
}
