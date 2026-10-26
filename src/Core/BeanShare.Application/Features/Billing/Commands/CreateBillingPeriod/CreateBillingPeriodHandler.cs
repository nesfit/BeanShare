using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Billing.Dtos;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Billing.Commands.CreateBillingPeriod;
public sealed class CreateBillingPeriodHandler : IRequestHandler<CreateBillingPeriodCommand, Result<BillingPeriodDto>>
{
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public CreateBillingPeriodHandler(
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

    public async Task<Result<BillingPeriodDto>> Handle(CreateBillingPeriodCommand command, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;
        var spaceId = new SpaceId(command.SpaceId);

        var spaceSpec = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);

        if (space == null)
        {
            return Result<BillingPeriodDto>.Failure(Error.SpaceNotFound(command.SpaceId));
        }

        if (!space.IsAdmin(currentUserId))
        {
            return Result<BillingPeriodDto>.Failure(Error.InsufficientSpacePrivileges("create billing periods"));
        }

        var overlapEnd = command.EndDate ?? BillingPeriod.OpenEndedSentinel;
        var hasOverlap = await _billingPeriodRepository.HasOverlappingPeriodAsync(
            spaceId, command.StartDate, overlapEnd, null, cancellationToken);

        if (hasOverlap)
        {
            return Result<BillingPeriodDto>.Failure(Error.BillingPeriodOverlap());
        }

        var spacePeriods = await _billingPeriodRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (BillingPeriodOrdering.CheckStartsAfterLastSettled(command.StartDate, spacePeriods) is { } orderError)
        {
            return Result<BillingPeriodDto>.Failure(orderError);
        }

        var billingPeriod = BillingPeriod.Create(
            spaceId,
            command.Name,
            command.StartDate,
            command.EndDate,
            currentUserId,
            _clock);

        await _billingPeriodRepository.AddAsync(billingPeriod, cancellationToken);

        var dto = new BillingPeriodDto(
            billingPeriod.Id.Value,
            billingPeriod.SpaceId.Value,
            billingPeriod.Name,
            billingPeriod.StartDate,
            billingPeriod.EndDate,
            billingPeriod.State.ToString(),
            billingPeriod.CreatedAt,
            billingPeriod.CreatedBy.Value,
            null,
            null,
            billingPeriod.ClosedAt,
            billingPeriod.ClosedBy?.Value,
            billingPeriod.SettledAt,
            billingPeriod.SettledBy?.Value,
            0,
            0m,
            null,
            null 
        );

        return Result<BillingPeriodDto>.Success(dto);
    }
}