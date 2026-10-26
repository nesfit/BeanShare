using BeanShare.Domain.Aggregates.Ledger;
using FluentValidation;

namespace BeanShare.Application.Features.Balances.Commands.CreateBalanceEntry;

public sealed class CreateBalanceEntryValidator : AbstractValidator<CreateBalanceEntryCommand>
{
    public CreateBalanceEntryValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Amount)
            .NotEqual(0m).WithMessage("Amount must not be zero")
            .InclusiveBetween(-BalanceEntry.MaxAbsoluteAmount, BalanceEntry.MaxAbsoluteAmount)
            .WithMessage($"Amount must be at most {BalanceEntry.MaxAbsoluteAmount:0} in absolute value");
        RuleFor(x => x.EffectiveDate).NotEmpty();
        RuleFor(x => x.Note)
            .NotEmpty().WithMessage("A note is required")
            .MaximumLength(BalanceEntry.MaxNoteLength);
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.CounterpartyUserId)
            .Must((cmd, counterparty) => counterparty != cmd.UserId)
            .WithMessage("The member and the counterparty must differ");
    }
}
