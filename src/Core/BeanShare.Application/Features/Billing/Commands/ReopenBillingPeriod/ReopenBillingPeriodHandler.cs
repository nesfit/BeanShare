using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Billing.Commands.ReopenBillingPeriod;
public sealed class ReopenBillingPeriodHandler : IRequestHandler<ReopenBillingPeriodCommand, Result>
{
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public ReopenBillingPeriodHandler(
        IBillingPeriodRepository billingPeriodRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext,
        IClock clock)
    {
        _billingPeriodRepository = billingPeriodRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result> Handle(ReopenBillingPeriodCommand command, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;
        var billingPeriodId = new BillingPeriodId(command.BillingPeriodId);

        var billingPeriod = await _billingPeriodRepository.GetByIdAsync(billingPeriodId, cancellationToken);
        if (billingPeriod == null)
            return Result.Failure(Error.BillingPeriodNotFound(command.BillingPeriodId));

        var spaceSpec = new SpaceByIdSpecification(billingPeriod.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);

        if (space == null)
            return Result.Failure(Error.SpaceNotFound(billingPeriod.SpaceId.Value));

        if (!space.IsAdmin(currentUserId))
            return Result.Failure(Error.InsufficientSpacePrivileges("reopen billing periods"));

        var spacePeriods = await _billingPeriodRepository.GetBySpaceIdAsync(space.Id, cancellationToken);
        var hasLaterPeriod = spacePeriods.Any(p => p.Id != billingPeriod.Id && p.StartDate > billingPeriod.StartDate);

        try
        {
            billingPeriod.Reopen(currentUserId, _clock, hasLaterPeriod);
            await _billingPeriodRepository.UpdateAsync(billingPeriod, cancellationToken);

            space.BumpLedgerVersion();
            await _spaceRepository.UpdateAsync(space, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.DomainError(ex.Message));
        }
    }
}
