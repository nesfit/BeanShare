using FluentValidation;

namespace BeanShare.Application.Features.Balances.Commands.DeleteBalanceEntry;

public sealed class DeleteBalanceEntryValidator : AbstractValidator<DeleteBalanceEntryCommand>
{
    public DeleteBalanceEntryValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
    }
}
