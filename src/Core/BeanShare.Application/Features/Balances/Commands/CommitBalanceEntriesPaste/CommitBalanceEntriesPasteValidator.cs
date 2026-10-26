using FluentValidation;

namespace BeanShare.Application.Features.Balances.Commands.CommitBalanceEntriesPaste;

public sealed class CommitBalanceEntriesPasteValidator : AbstractValidator<CommitBalanceEntriesPasteCommand>
{
    public CommitBalanceEntriesPasteValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.Text).NotEmpty().WithMessage("Paste at least one row");
        RuleFor(x => x.Kind).IsInEnum();
    }
}
