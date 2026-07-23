using FluentValidation;

namespace BeanShare.Application.Features.ActiveQrCodes.Commands;

public sealed class ReassignActiveQrCodeCommandValidator : AbstractValidator<ReassignActiveQrCodeCommand>
{
    public ReassignActiveQrCodeCommandValidator()
    {
        RuleFor(x => x.QrCodeId)
            .NotEmpty()
            .WithMessage($"{nameof(ReassignActiveQrCodeCommand.QrCodeId)} is required");

        RuleFor(x => x.ProductName)
            .NotEmpty()
            .WithMessage("Product name is required")
            .MaximumLength(100)
            .WithMessage("Product name cannot exceed 100 characters");

        RuleFor(x => x.ProductBrand)
            .NotEmpty()
            .WithMessage("Product brand is required")
            .MaximumLength(50)
            .WithMessage("Product brand cannot exceed 50 characters");

        RuleFor(x => x.RecipeName)
            .MaximumLength(100)
            .WithMessage("Recipe name cannot exceed 100 characters");

        RuleFor(x => x.DefaultGrams)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Default grams cannot be negative");

        RuleFor(x => x.RecipeName)
            .NotEmpty()
            .When(x => x.DefaultGrams > 0)
            .WithMessage("Recipe name is required when default grams is set");
    }
}
