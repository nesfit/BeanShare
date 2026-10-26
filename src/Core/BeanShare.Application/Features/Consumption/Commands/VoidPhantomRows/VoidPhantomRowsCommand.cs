using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using FluentValidation;

namespace BeanShare.Application.Features.Consumption.Commands.VoidPhantomRows;

/// <summary>
/// Removes phantom write-off rows without returning anything to stock (DS-42): a write-off share's grams
/// move to its bag's voided counter, so no bag revives; an unstamped row touches no bag. All or nothing.
/// The guard cannot prove a row is phantom; the cutover runbook decides that.
/// </summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record VoidPhantomRowsCommand(Guid SpaceId, IReadOnlyList<Guid> ConsumptionIds, string Reason)
    : IAuthorize, ICommand<Result>;

public sealed class VoidPhantomRowsCommandValidator : AbstractValidator<VoidPhantomRowsCommand>
{
    public const int MaxRows = 1000;
    public const int MaxReasonLength = 200;

    public VoidPhantomRowsCommandValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.ConsumptionIds)
            .NotNull()
            .Must(ids => ids is { Count: >= 1 and <= MaxRows })
            .WithMessage($"Select 1–{MaxRows} rows");
        RuleFor(x => x.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r) && r.Trim().Length <= MaxReasonLength)
            .WithMessage($"A reason of 1–{MaxReasonLength} characters is required");
    }
}
