using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Billing.Commands.DeleteBillingPeriod;

public sealed class DeleteBillingPeriodHandler : IRequestHandler<DeleteBillingPeriodCommand, Result>
{
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;

    public DeleteBillingPeriodHandler(
        IBillingPeriodRepository billingPeriodRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext)
    {
        _billingPeriodRepository = billingPeriodRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
    }

    public async Task<Result> Handle(DeleteBillingPeriodCommand command, CancellationToken cancellationToken)
    {
        var billingPeriod = await _billingPeriodRepository.GetByIdAsync(new BillingPeriodId(command.BillingPeriodId), cancellationToken);
        if (billingPeriod == null)
            return Result.Failure(Error.BillingPeriodNotFound(command.BillingPeriodId));

        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(billingPeriod.SpaceId), cancellationToken);
        if (space == null)
            return Result.Failure(Error.SpaceNotFound(billingPeriod.SpaceId.Value));

        if (!space.IsAdmin(_userContext.CurrentUserId))
            return Result.Failure(Error.InsufficientSpacePrivileges("delete billing periods"));

        if (billingPeriod.State != BillingState.Draft)
            return Result.Failure(Error.PeriodNotDeletable(billingPeriod.State.ToString()));

        // The period's Version token makes a racing transition fail instead of deleting a non-draft.
        _billingPeriodRepository.Remove(billingPeriod);

        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);

        return Result.Success();
    }
}
