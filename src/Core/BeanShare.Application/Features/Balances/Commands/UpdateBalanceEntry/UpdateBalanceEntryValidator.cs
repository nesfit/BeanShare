using BeanShare.Domain.Aggregates.Ledger;
using FluentValidation;

namespace BeanShare.Application.Features.Balances.Commands.UpdateBalanceEntry;

public sealed class UpdateBalanceEntryValidator : AbstractValidator<UpdateBalanceEntryCommand>
{
    public UpdateBalanceEntryValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CounterpartyUserId)
            .NotEmpty()
            .NotEqual(x => x.UserId).WithMessage("The member and the counterparty must differ");
        RuleFor(x => x.Amount)
            .NotEqual(0m).WithMessage("Amount must not be zero")
            .InclusiveBetween(-BalanceEntry.MaxAbsoluteAmount, BalanceEntry.MaxAbsoluteAmount)
            .WithMessage($"Amount must be at most {BalanceEntry.MaxAbsoluteAmount:0} in absolute value");
        RuleFor(x => x.EffectiveDate).NotEmpty();
        RuleFor(x => x.Note)
            .NotEmpty().WithMessage("A note is required")
            .MaximumLength(BalanceEntry.MaxNoteLength);
        RuleFor(x => x.Kind).IsInEnum();
    }
}
