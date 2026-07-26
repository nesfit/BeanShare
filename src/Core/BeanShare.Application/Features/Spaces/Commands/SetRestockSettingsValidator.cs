using BeanShare.Domain.Aggregates.Space;
using FluentValidation;

namespace BeanShare.Application.Features.Spaces.Commands;

public sealed class SetRestockSettingsValidator : AbstractValidator<SetRestockSettingsCommand>
{
    public SetRestockSettingsValidator()
    {
        RuleFor(x => x.SpaceId)
            .NotEmpty()
            .WithMessage($"{nameof(SetRestockSettingsCommand.SpaceId)} is required");

        RuleFor(x => x.LeadTimeDays)
            .InclusiveBetween(0, 90)
            .WithMessage("Lead time must be between 0 and 90 days");

        RuleFor(x => x.MinOrderValue)
            .InclusiveBetween(0m, Space.MaxRestockMinOrderValue)
            .WithMessage($"Minimum order value must be between 0 and {Space.MaxRestockMinOrderValue:0}");
    }
}
