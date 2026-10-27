using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Settlement.Queries.GetSettlementById;

/// <summary>
/// Reads a settlement from its stored snapshot: a ledger settlement never recomputes anything; a
/// legacy one shows its frozen Paid, falling back to the old purchase-based figure only when the
/// backfill has not frozen it yet.
/// </summary>
public sealed class GetSettlementByIdHandler : IRequestHandler<GetSettlementByIdQuery, Result<SettlementDto>>
{
    private readonly ISettlementRepository _settlementRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;

    public GetSettlementByIdHandler(
        ISettlementRepository settlementRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ISpaceRepository spaceRepository,
        ICoffeeStockRepository coffeeStockRepository,
        IUserService userService,
        IUserContext userContext)
    {
        _settlementRepository = settlementRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _spaceRepository = spaceRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _userService = userService;
        _userContext = userContext;
    }

    public async Task<Result<SettlementDto>> Handle(GetSettlementByIdQuery request, CancellationToken cancellationToken)
    {
        var settlement = await _settlementRepository.GetByIdAsync(request.Id, cancellationToken);

        if (settlement is null)
        {
            return Result<SettlementDto>.Failure(Error.ValidationFailure("Settlement", $"Settlement with ID {request.Id.Value} not found"));
        }

        var billingPeriod = await _billingPeriodRepository.GetByIdAsync(settlement.BillingPeriodId, cancellationToken);
        if (billingPeriod is null)
        {
            return Result<SettlementDto>.Failure(Error.BillingPeriodNotFound(settlement.BillingPeriodId.Value));
        }

        var space = await _spaceRepository.GetSingleBySpecAsync(
            new SpaceByIdSpec(settlement.SpaceId),
            cancellationToken);

        if (space is null || !space.HasMember(_userContext.CurrentUserId))
        {
            return Result<SettlementDto>.Failure(Error.InsufficientSpacePrivileges("view settlement"));
        }

        IReadOnlyDictionary<UserId, decimal>? legacyPaid = null;
        if (!settlement.IsLedger && settlement.Lines.Any(l => l.LegacyAmountPaid is null))
        {
            var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(settlement.SpaceId, cancellationToken);
            legacyPaid = LegacyPaid.Compute(coffeeStock, billingPeriod, settlement.Currency);
        }

        var users = await _userService.GetByIdsAsync(SettlementDtoMapper.UserIdsOf(settlement), cancellationToken);
        var dto = SettlementDtoMapper.ToDto(settlement, billingPeriod, users.ToDictionary(u => u.Id), legacyPaid);

        return Result<SettlementDto>.Success(dto);
    }
}

public sealed class SpaceByIdSpec : ISpec<Domain.Aggregates.Space.Space>
{
    private readonly SpaceId _spaceId;

    public SpaceByIdSpec(SpaceId spaceId)
    {
        _spaceId = spaceId;
    }

    public System.Linq.Expressions.Expression<Func<Domain.Aggregates.Space.Space, bool>> Criteria =>
        space => space.Id == _spaceId;

    public string? Reason => $"Space with ID {_spaceId.Value}";
}
