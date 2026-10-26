using BeanShare.Domain.Common;
using FluentValidation;

namespace BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;

public sealed class UpdateLedgerSettingsValidator : AbstractValidator<UpdateLedgerSettingsCommand>
{
    public UpdateLedgerSettingsValidator(IClock clock)
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.TreasurerUserId)
            .Must(id => id != Guid.Empty)
            .When(x => x.TreasurerUserId.HasValue)
            .WithMessage("The treasurer is invalid");
        RuleFor(x => x.TreasurerUserId)
            .Null()
            .When(x => x.ClearTreasurer)
            .WithMessage("Either set or clear the treasurer, not both");
        RuleFor(x => x.LedgerStartDate)
            .Must(date => UtcDate.Of(date!.Value) <= clock.UtcNow.Date)
            .When(x => x.LedgerStartDate.HasValue)
            .WithMessage("The ledger start date cannot be in the future");
        RuleFor(x => x.LedgerStartDate)
            .Null()
            .When(x => x.ClearLedgerStartDate)
            .WithMessage("Either set or clear the ledger start date, not both");
    }
}
