using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Settlement.Queries.GetSpaceSettlements;
public sealed class GetSpaceSettlementsHandler : IRequestHandler<GetSpaceSettlementsQuery, Result<List<SettlementSummaryDto>>>
{
    private readonly ISettlementRepository _settlementRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;

    public GetSpaceSettlementsHandler(
        ISettlementRepository settlementRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext)
    {
        _settlementRepository = settlementRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
    }

    public async Task<Result<List<SettlementSummaryDto>>> Handle(GetSpaceSettlementsQuery request, CancellationToken cancellationToken)
    {
        var space = await _spaceRepository.GetByIdAsync(request.SpaceId, cancellationToken);
        if (space is null || !space.HasMember(_userContext.CurrentUserId))
        {
            return Result<List<SettlementSummaryDto>>.Failure(Error.InsufficientSpacePrivileges("view settlements"));
        }

        var settlements = await _settlementRepository.GetBySpaceIdAsync(request.SpaceId, cancellationToken);

        var summaries = new List<SettlementSummaryDto>();

        foreach (var settlement in settlements)
        {
            var billingPeriod = await _billingPeriodRepository.GetByIdAsync(settlement.BillingPeriodId, cancellationToken);
            var billingPeriodName = billingPeriod?.Name ?? "Unknown Period";

            var currentUserId = _userContext.CurrentUserId;
            var userLine = settlement.Lines.FirstOrDefault(l => l.UserId == currentUserId);
            var yourShare = userLine?.AmountDue.Amount ?? 0;
            var yourNet = settlement.IsLedger ? userLine?.NetAmount ?? 0m : (decimal?)null;

            var summary = new SettlementSummaryDto(
                settlement.Id.Value,
                billingPeriodName,
                settlement.GeneratedAt,
                settlement.TotalAmount,
                settlement.Currency,
                settlement.Status.ToString(),
                settlement.ConfirmedLinesCount,
                settlement.TotalLinesCount,
                settlement.Lines.Count,
                yourShare,
                yourNet,
                settlement.Model?.ToString(),
                settlement.SupersededBySettlementId.HasValue
            );

            summaries.Add(summary);
        }

        return Result<List<SettlementSummaryDto>>.Success(summaries);
    }
}