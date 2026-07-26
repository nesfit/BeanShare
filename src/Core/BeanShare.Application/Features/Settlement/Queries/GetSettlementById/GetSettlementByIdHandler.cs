using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Settlement.Queries.GetSettlementById;
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

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(settlement.SpaceId, cancellationToken);
        var periodPurchases = coffeeStock?.Purchases
            .Where(p => p.PurchasedAt >= billingPeriod.StartDate && p.PurchasedAt <= billingPeriod.EndDate)
            .ToList() ?? new();

        if (!periodPurchases.Any())
        {
            periodPurchases = coffeeStock?.Purchases
                .Where(p => p.PurchasedAt <= billingPeriod.EndDate)
                .ToList() ?? new();
        }

        var purchasesByUser = periodPurchases
            .Where(p => p.Cost.Currency == settlement.Currency)
            .GroupBy(p => p.PurchasedBy)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Cost.Amount));

        var userIds = settlement.Lines.Select(l => l.UserId).Distinct().ToList();
        var users = await _userService.GetByIdsAsync(userIds, cancellationToken);
        var userLookup = users.ToDictionary(u => u.Id);

        var totalConsumption = settlement.Lines.Sum(l => l.TotalCoffeeGrams);

        var lines = new List<SettlementLineDto>();
        foreach (var line in settlement.Lines)
        {
            userLookup.TryGetValue(line.UserId, out var user);
            var userName = user?.Name ?? $"User {line.UserId.Value}";
            var userEmail = user?.Email ?? $"user{line.UserId.Value}@example.com";

            var consumptionPercentage = totalConsumption > 0
                ? (line.TotalCoffeeGrams / totalConsumption) * 100
                : 0;

            var amountPaid = purchasesByUser.TryGetValue(line.UserId, out var paid) ? paid : 0m;
            var netBalance = amountPaid - line.AmountDue.Amount;

            var lineDto = new SettlementLineDto(
                line.UserId.Value,
                userName,
                userEmail,
                line.TotalCoffeeGrams,
                line.TotalMilkMl ?? 0,
                line.AmountDue.Amount,
                amountPaid,
                netBalance,
                line.AmountDue.Currency,
                consumptionPercentage,
                line.IsConfirmed,
                line.Confirmation?.ConfirmedBy.Value,
                line.Confirmation?.ConfirmedAt,
                user?.RevolutTag
            );

            lines.Add(lineDto);
        }

        var dto = new SettlementDto(
            settlement.Id.Value,
            settlement.SpaceId.Value,
            settlement.BillingPeriodId.Value,
            billingPeriod.Name,
            billingPeriod.StartDate,
            billingPeriod.EndDate,
            settlement.GeneratedAt,
            settlement.GeneratedBy.Value,
            settlement.TotalAmount,
            settlement.Currency,
            settlement.Status.ToString(),
            settlement.CompletedAt,
            settlement.ConfirmedLinesCount,
            settlement.TotalLinesCount,
            lines
        );

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