using FluentValidation;

namespace BeanShare.Application.Features.Spaces.Commands;

public sealed class CreatePlaceholderMemberValidator : AbstractValidator<CreatePlaceholderMemberCommand>
{
    public CreatePlaceholderMemberValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Services.PlaceholderMembers.MaxNameLength);
        RuleFor(x => x.Email).MaximumLength(254);
    }
}

public sealed class UpdatePlaceholderMemberValidator : AbstractValidator<UpdatePlaceholderMemberCommand>
{
    public UpdatePlaceholderMemberValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Services.PlaceholderMembers.MaxNameLength);
        RuleFor(x => x.Email).MaximumLength(254);
    }
}

public sealed class MergePlaceholderValidator : AbstractValidator<MergePlaceholderCommand>
{
    public MergePlaceholderValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.PlaceholderUserId).NotEmpty();
        RuleFor(x => x.TargetUserId).NotEmpty()
            .NotEqual(x => x.PlaceholderUserId).WithMessage("A placeholder cannot be merged into itself");
    }
}
