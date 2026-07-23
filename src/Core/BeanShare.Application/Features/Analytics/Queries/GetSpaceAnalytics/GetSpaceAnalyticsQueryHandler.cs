using BeanShare.Application.Abstractions;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Analytics.Queries.GetSpaceAnalytics;
public sealed class GetSpaceAnalyticsQueryHandler : IRequestHandler<GetSpaceAnalyticsQuery, SpaceAnalyticsDto>
{
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ICostCalculationService _costCalculationService;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public GetSpaceAnalyticsQueryHandler(
        IConsumptionRepository consumptionRepository,
        ISpaceRepository spaceRepository,
        ICoffeeStockRepository coffeeStockRepository,
        ICostCalculationService costCalculationService,
        IUserService userService,
        IUserContext userContext,
        IClock clock)
    {
        _consumptionRepository = consumptionRepository;
        _spaceRepository = spaceRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _costCalculationService = costCalculationService;
        _userService = userService;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<SpaceAnalyticsDto> Handle(GetSpaceAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var fromDate = request.FromDate ?? DateTime.MinValue;
        var toDate = request.ToDate ?? now;

        var spaceSpec = new SpaceByIdSpecification(request.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);

        if (space == null)
        {
            return new SpaceAnalyticsDto
            {
                TotalMembers = 0,
                TotalConsumptionsThisMonth = 0,
                TotalConsumptionsAllTime = 0
            };
        }

        if (!space.HasMember(_userContext.CurrentUserId))
        {
            return new SpaceAnalyticsDto
            {
                TotalMembers = 0,
                TotalConsumptionsThisMonth = 0,
                TotalConsumptionsAllTime = 0
            };
        }

        var allConsumptions = await _consumptionRepository.GetBySpaceIdAsync(request.SpaceId, cancellationToken);

        var consumptions = allConsumptions
            .Where(c => c.ConsumedAt >= fromDate && c.ConsumedAt <= toDate)
            .ToList();

        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var consumptionsThisMonth = allConsumptions.Where(c => c.ConsumedAt >= startOfMonth).ToList();

        var consumerGroups = consumptions
            .GroupBy(c => c.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                CupCount = g.Count(),
                TotalGrams = g.Sum(c => c.Quantity.Grams)
            })
            .OrderByDescending(x => x.CupCount)
            .ToList();

        var topConsumers = new List<TopConsumerDto>();

        if (consumerGroups.Any())
        {
            var userIds = consumerGroups.Select(x => x.UserId).ToList();
            var users = await _userService.GetByIdsAsync(userIds, cancellationToken);

            foreach (var consumer in consumerGroups)
            {
                var user = users.FirstOrDefault(u => u.Id == consumer.UserId);
                var cost = await _costCalculationService.CalculateTotalCostAsync(
                    request.SpaceId,
                    consumer.TotalGrams,
                    cancellationToken);

                topConsumers.Add(new TopConsumerDto
                {
                    UserId = consumer.UserId.Value,
                    UserName = user?.Name ?? "Unknown User",
                    CupCount = consumer.CupCount,
                    TotalCost = cost
                });
            }
        }

        var totalConsumptionCount = consumptions.Count;
        var coffeeGroups = consumptions
            .GroupBy(c => c.Product.Name)
            .Select(g => new PopularCoffeeDto
            {
                CoffeeName = g.Key,
                ConsumptionCount = g.Count(),
                TotalGrams = g.Sum(c => c.Quantity.Grams),
                Percentage = totalConsumptionCount > 0 ? (double)g.Count() / totalConsumptionCount * 100 : 0
            })
            .OrderByDescending(x => x.ConsumptionCount)
            .Take(5)
            .ToList();

        var last30Days = now.AddDays(-30).Date.ToUniversalTime();
        var dailyTrend = consumptions
            .Where(c => c.ConsumedAt >= last30Days)
            .GroupBy(c => c.ConsumedAt.Date)
            .ToDictionary(g => g.Key, g => g.Count())
            .OrderBy(kvp => kvp.Key)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var hourlyPattern = consumptions
            .GroupBy(c => c.ConsumedAt.Hour)
            .ToDictionary(g => g.Key, g => g.Count())
            .OrderBy(kvp => kvp.Key)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var stock = await _coffeeStockRepository.GetBySpaceIdAsync(request.SpaceId, cancellationToken);
        var stockGrams = stock?.StockLevels.Sum(sl => sl.CurrentStock.Grams) ?? 0;
        // Value the stock via the total-cost method rather than multiplying a per-gram rate
        // that has already been rounded to whole cents (which inflates the total).
        var stockValue = await _costCalculationService.CalculateTotalCostAsync(request.SpaceId, stockGrams, cancellationToken);

        var totalGramsThisMonth = consumptionsThisMonth.Sum(c => c.Quantity.Grams);
        var totalCostThisMonth = await _costCalculationService.CalculateTotalCostAsync(
            request.SpaceId,
            totalGramsThisMonth,
            cancellationToken);

        return new SpaceAnalyticsDto
        {
            TotalMembers = space.Members.Count,
            TotalConsumptionsThisMonth = consumptionsThisMonth.Count,
            TotalConsumptionsAllTime = allConsumptions.Count(),
            TopConsumers = topConsumers,
            PopularCoffeeTypes = coffeeGroups,
            CurrentStockValue = stockValue,
            CurrentStockGrams = stockGrams,
            DailyConsumptionTrend = dailyTrend,
            HourlyConsumptionPattern = hourlyPattern,
            TotalCostThisMonth = totalCostThisMonth
        };
    }
}
