using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.ActiveQrCodes.Dtos;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.ActiveQrCodes.Commands;

public sealed class ReassignActiveQrCodeCommandHandler : IRequestHandler<ReassignActiveQrCodeCommand, Result<ActiveQrCodeDto>>
{
    private readonly IActiveQrCodeRepository _qrCodeRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;

    public ReassignActiveQrCodeCommandHandler(
        IActiveQrCodeRepository qrCodeRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext)
    {
        _qrCodeRepository = qrCodeRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
    }

    public async Task<Result<ActiveQrCodeDto>> Handle(ReassignActiveQrCodeCommand command, CancellationToken cancellationToken)
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

        if (!Enum.TryParse<CoffeeType>(command.ProductType, true, out var coffeeType))
        {
            return Result<ActiveQrCodeDto>.Failure(Error.InvalidCoffeeType());
        }

        try
        {
            var product = CoffeeProduct.Create(command.ProductName, command.ProductBrand, coffeeType);
            qrCode.Reassign(product, command.RecipeName, command.DefaultGrams);
            await _qrCodeRepository.UpdateAsync(qrCode, cancellationToken);

            return Result<ActiveQrCodeDto>.Success(ActiveQrCodeMapper.ToDto(qrCode));
        }
        catch (ArgumentException ex)
        {
            return Result<ActiveQrCodeDto>.Failure(Error.ValidationFailure(nameof(ReassignActiveQrCodeCommand), ex.Message));
        }
    }
}
