using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Billing.Commands.OpenBillingPeriod;
public sealed class OpenBillingPeriodHandler : IRequestHandler<OpenBillingPeriodCommand, Result>
{
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public OpenBillingPeriodHandler(
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

    public async Task<Result> Handle(OpenBillingPeriodCommand command, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;
        var billingPeriodId = new BillingPeriodId(command.BillingPeriodId);

        var billingPeriod = await _billingPeriodRepository.GetByIdAsync(billingPeriodId, cancellationToken);
        if (billingPeriod == null)
        {
            return Result.Failure(Error.BillingPeriodNotFound(command.BillingPeriodId));
        }

        // Verify user is admin of the space
        var spaceSpec = new SpaceByIdSpecification(billingPeriod.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);

        if (space == null)
        {
            return Result.Failure(Error.SpaceNotFound(billingPeriod.SpaceId.Value));
        }

        if (!space.IsAdmin(currentUserId))
        {
            return Result.Failure(Error.InsufficientSpacePrivileges("open billing periods"));
        }

        var spacePeriods = await _billingPeriodRepository.GetBySpaceIdAsync(space.Id, cancellationToken);
        if (billingPeriod.State == Domain.Enums.BillingState.Draft
            && BillingPeriodOrdering.CheckStartsAfterLastSettled(billingPeriod.StartDate, spacePeriods) is { } orderError)
        {
            return Result.Failure(orderError);
        }

        try
        {
            billingPeriod.Open(currentUserId, _clock);
            await _billingPeriodRepository.UpdateAsync(billingPeriod, cancellationToken);

            // Serialised with creates, reopens, closes and generation through the space's ledger token.
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