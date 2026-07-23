using BeanShare.Domain.Entities;
using FluentValidation;

namespace BeanShare.Application.Features.ActiveQrCodes.Commands;

public sealed class RenameActiveQrCodeCommandValidator : AbstractValidator<RenameActiveQrCodeCommand>
{
    public RenameActiveQrCodeCommandValidator()
    {
        RuleFor(x => x.QrCodeId)
            .NotEmpty()
            .WithMessage($"{nameof(RenameActiveQrCodeCommand.QrCodeId)} is required");

        RuleFor(x => x.Label)
            .NotEmpty()
            .WithMessage("Label is required")
            .MaximumLength(ActiveQrCode.MaxLabelLength)
            .WithMessage($"Label cannot exceed {ActiveQrCode.MaxLabelLength} characters");
    }
}
