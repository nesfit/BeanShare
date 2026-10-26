using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Balances.Queries.GetLedgerStartImpact;

public sealed class GetLedgerStartImpactHandler : IRequestHandler<GetLedgerStartImpactQuery, Result<LedgerStartImpactDto>>
{
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;

    public GetLedgerStartImpactHandler(
        IConsumptionRepository consumptionRepository,
        IBillingPeriodRepository billingPeriodRepository)
    {
        _consumptionRepository = consumptionRepository;
        _billingPeriodRepository = billingPeriodRepository;
    }

    public async Task<Result<LedgerStartImpactDto>> Handle(GetLedgerStartImpactQuery query, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(query.SpaceId);
        var start = UtcDate.Of(query.LedgerStartDate);

        var settledPeriodIds = (await _billingPeriodRepository.GetBySpaceIdAsync(spaceId, cancellationToken))
            .Where(p => p.State == BillingState.Settled)
            .Select(p => p.Id)
            .ToHashSet();

        var cupsBefore = await _consumptionRepository.GetBySpecAsync(
            new ConsumptionsBySpaceAndDateRangeSpecification(spaceId, endDate: start.AddTicks(-1)), cancellationToken);
        var unbilled = cupsBefore.Count(c => c.BillingPeriodId is not { } periodId || !settledPeriodIds.Contains(periodId));

        return Result<LedgerStartImpactDto>.Success(new LedgerStartImpactDto(start, unbilled));
    }
}
