using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Billing.Dtos;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Billing.Queries.GetSpaceBillingPeriods;
public sealed class GetSpaceBillingPeriodsHandler : IRequestHandler<GetSpaceBillingPeriodsQuery, Result<List<BillingPeriodSummaryDto>>>
{
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public GetSpaceBillingPeriodsHandler(
        IBillingPeriodRepository billingPeriodRepository,
        ISpaceRepository spaceRepository,
        IConsumptionRepository consumptionRepository,
        IUserContext userContext,
        IClock clock)
    {
        _billingPeriodRepository = billingPeriodRepository;
        _spaceRepository = spaceRepository;
        _consumptionRepository = consumptionRepository;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<List<BillingPeriodSummaryDto>>> Handle(
        GetSpaceBillingPeriodsQuery query,
        CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;
        var spaceId = new SpaceId(query.SpaceId);

        var spaceSpec = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);

        if (space == null)
        {
            return Result<List<BillingPeriodSummaryDto>>.Failure(Error.SpaceNotFound(query.SpaceId));
        }

        if (!space.HasMember(currentUserId))
        {
            return Result<List<BillingPeriodSummaryDto>>.Failure(
                Error.InsufficientSpacePrivileges("view billing periods"));
        }

        var billingPeriods = await _billingPeriodRepository.GetBySpaceIdAsync(spaceId, cancellationToken);

        var consumptions = await _consumptionRepository.GetBySpaceIdAsync(spaceId, cancellationToken);

        var summaries = new List<BillingPeriodSummaryDto>();

        foreach (var period in billingPeriods.OrderByDescending(p => p.StartDate))
        {
            // A historical period's cups live in its import, never in the live consumption table.
            var periodConsumptions = period.IsHistorical
                ? []
                : consumptions
                    .Where(c => c.ConsumedAt >= period.StartDate && c.ConsumedAt <= period.EndDate)
                    .ToList();

            var daysRemaining = period.State == BillingState.Open
                ? Math.Max(0, (int)(period.EndDate - _clock.UtcNow).TotalDays)
                : 0;

            summaries.Add(new BillingPeriodSummaryDto(
                period.Id.Value,
                period.Name,
                period.StartDate,
                period.EndDate,
                period.State.ToString(),
                daysRemaining,
                periodConsumptions.Count,
                periodConsumptions.Sum(c => c.Quantity.Grams),
                period.IsHistorical,
                period.HistoryImportId?.Value
            ));
        }

        return Result<List<BillingPeriodSummaryDto>>.Success(summaries);
    }
}