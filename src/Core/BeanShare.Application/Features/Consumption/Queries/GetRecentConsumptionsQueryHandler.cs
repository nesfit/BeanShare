using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Consumption.Dtos;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Consumption.Queries;
public sealed class GetRecentConsumptionsQueryHandler
    : IRequestHandler<GetRecentConsumptionsQuery, Result<GetRecentConsumptionsResult>>
{
    private const int RecentEntriesLimit = 20;

    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public GetRecentConsumptionsQueryHandler(
        IConsumptionRepository consumptionRepository,
        ICoffeeStockRepository coffeeStockRepository,
        ISpaceRepository spaceRepository,
        IBillingPeriodRepository billingPeriodRepository,
        IUserRepository userRepository,
        IUserContext userContext,
        IClock clock)
    {
        _consumptionRepository = consumptionRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _spaceRepository = spaceRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _userRepository = userRepository;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<GetRecentConsumptionsResult>> Handle(
        GetRecentConsumptionsQuery request,
        CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(request.SpaceId);
        var userId = _userContext.CurrentUserId;

        var spaceSpec = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);
        if (space == null)
        {
            return Result<GetRecentConsumptionsResult>.Failure(Error.SpaceNotFound(request.SpaceId));
        }

        if (!space.HasMember(userId))
        {
            return Result<GetRecentConsumptionsResult>.Failure(Error.InsufficientSpacePrivileges("view consumptions"));
        }

        UserId? forUserId = request.ForUserId.HasValue ? new UserId(request.ForUserId.Value) : null;
        var recentRaw = await _consumptionRepository.GetRecentBySpaceIdAsync(
            spaceId, RecentEntriesLimit, forUserId, cancellationToken);

        var userIds = recentRaw.Select(e => e.UserId).Distinct().ToList();
        var userNames = new Dictionary<UserId, string>();
        foreach (var uid in userIds)
        {
            var user = await _userRepository.GetByIdAsync(uid, cancellationToken);
            userNames[uid] = user?.Name ?? "Unknown";
        }

        var billingPeriods = await _billingPeriodRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        var editablePeriodIds = billingPeriods
            .Where(bp => bp.State == BillingState.Draft || bp.State == BillingState.Open)
            .Select(bp => bp.Id)
            .ToHashSet();

        // A cup can change while its period is Draft or Open. Cups before the ledger start are never billed, so
        // they cannot be changed either.
        bool MayChange(ConsumptionEntry e) =>
            !(space.LedgerStartDate is { } ledgerStart && e.ConsumedAt < ledgerStart)
            && (e.BillingPeriodId == null || editablePeriodIds.Contains(e.BillingPeriodId.Value));

        var recentEntries = recentRaw
            .Select(e => new ConsumptionEntryDto
            {
                Id = e.Id.Value,
                SpaceId = e.SpaceId.Value,
                UserId = e.UserId.Value,
                UserName = userNames.GetValueOrDefault(e.UserId, "Unknown"),
                ProductName = e.Product?.Name ?? "Unknown",
                ProductBrand = e.Product?.Brand ?? "Unknown",
                ProductType = e.Product?.Type.ToString() ?? "Unknown",
                QuantityGrams = e.Quantity.Grams,
                RemainingGrams = 0,
                ConsumedAt = e.ConsumedAt,
                CreatedAt = e.CreatedAt,
                PresetId = e.PresetId?.Value,
                PresetName = e.PresetName,
                CostAmount = e.Cost?.Amount,
                CostCurrency = e.Cost?.Currency.Code,
                // A write-off share can only be deleted; the edit command refuses it.
                CanEdit = MayChange(e) && !e.IsWriteOff,
                CanDelete = MayChange(e)
            })
            .ToList();

        var allEntries = await _consumptionRepository.GetBySpaceIdAsync(spaceId, cancellationToken);

        var today = _clock.UtcNow.Date;
        var myEntriesToday = allEntries
            .Where(e => e.UserId == userId && e.ConsumedAt.Date == today)
            .ToList();

        var myCupsToday = myEntriesToday.Count;

        var myAllEntries = allEntries.Where(e => e.UserId == userId).ToList();
        var myTotalGrams = myAllEntries.Sum(e => e.Quantity.Grams);
        var myTotalCost = 0m;

        // Calculate remaining stock from actual stock data
        var remainingStock = 0;
        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock != null)
        {
            remainingStock = (int)coffeeStock.TotalCurrentStock.Grams;

            // Calculate cost per gram from stock purchases to estimate user's total cost
            var totalPurchaseCost = coffeeStock.Purchases.Sum(p => p.Cost.Amount);
            var totalPurchasedGrams = coffeeStock.Purchases.Sum(p => p.Quantity.Grams);
            if (totalPurchasedGrams > 0 && myTotalGrams > 0)
            {
                var costPerGram = totalPurchaseCost / totalPurchasedGrams;
                myTotalCost = costPerGram * myTotalGrams;
            }
        }

        var result = new GetRecentConsumptionsResult(
            recentEntries,
            myCupsToday,
            myTotalCost,
            remainingStock
        );

        return Result<GetRecentConsumptionsResult>.Success(result);
    }
}
