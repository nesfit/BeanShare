using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Queries.GetRestockForecast;

public sealed class GetRestockForecastQueryHandler : IRequestHandler<GetRestockForecastQuery, Result<RestockForecastDto>>
{
    // Batch-ordering defaults: order ~1 week ahead of running out, worthwhile basket ~2000.
    private const int LeadTimeDays = 7;
    private const int SafetyBufferDays = 7;     // "soon" window = lead time + buffer
    private const int ForecastWindowDays = 30;  // consumption sample used for velocity
    private const int CoverageDays = 30;        // reorder enough to last this long beyond lead time
    private const decimal MinOrderValue = 2000m;

    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IClock _clock;

    public GetRestockForecastQueryHandler(
        ICoffeeStockRepository coffeeStockRepository,
        IConsumptionRepository consumptionRepository,
        ISpaceRepository spaceRepository,
        IClock clock)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _consumptionRepository = consumptionRepository;
        _spaceRepository = spaceRepository;
        _clock = clock;
    }

    public async Task<Result<RestockForecastDto>> Handle(GetRestockForecastQuery query, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(query.SpaceId);
        var space = await _spaceRepository.GetByIdAsync(spaceId, cancellationToken);
        var currency = space?.Currency.Code ?? "USD";

        var empty = new RestockForecastDto([], 0, 0m, currency, LeadTimeDays, MinOrderValue, false);

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock is null)
        {
            return Result<RestockForecastDto>.Success(empty);
        }

        var now = _clock.UtcNow;
        var windowStart = now.AddDays(-ForecastWindowDays);

        var cups = await _consumptionRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        var consumedByKey = cups
            .Where(c => c.ConsumedAt >= windowStart)
            .GroupBy(c => Key(c.Product))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Quantity.Grams));

        var lastPurchaseByKey = coffeeStock.Purchases
            .GroupBy(p => Key(p.Product))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.PurchasedAt).First());

        var items = new List<RestockItemDto>();
        decimal basket = 0m;
        var needs = 0;

        foreach (var level in coffeeStock.StockLevels)
        {
            var key = Key(level.Product);
            var onHand = level.CurrentStock.Grams;
            var consumed = consumedByKey.GetValueOrDefault(key, 0m);
            var velocity = (double)consumed / ForecastWindowDays;

            int? daysToEmpty = velocity > 0.01 ? (int)Math.Floor((double)onHand / velocity) : null;
            DateTime? runOut = daysToEmpty.HasValue ? now.AddDays(daysToEmpty.Value) : null;

            var status = daysToEmpty switch
            {
                null => "ok",
                <= LeadTimeDays => "critical",
                _ when daysToEmpty <= LeadTimeDays + SafetyBufferDays => "soon",
                _ => "ok",
            };

            var suggestedGrams = 0;
            decimal? estCost = null;

            if (status != "ok")
            {
                needs++;

                if (lastPurchaseByKey.TryGetValue(key, out var last))
                {
                    var bagSize = last.Quantity.Grams > 0 ? last.Quantity.Grams : 1000m;
                    var target = (decimal)(velocity * (LeadTimeDays + CoverageDays));
                    var needed = Math.Max(0m, target - onHand);
                    var bags = Math.Clamp((int)Math.Ceiling(needed / bagSize), 1, 20);
                    suggestedGrams = (int)(bags * bagSize);
                    estCost = bags * last.Cost.Amount;
                    basket += estCost.Value;
                }
            }

            items.Add(new RestockItemDto(
                level.Product.DisplayName,
                level.Product.Type.ToString(),
                onHand,
                Math.Round(velocity, 2),
                daysToEmpty,
                runOut,
                status,
                suggestedGrams,
                estCost));
        }

        var ordered = items
            .OrderBy(i => i.Status == "critical" ? 0 : i.Status == "soon" ? 1 : 2)
            .ThenBy(i => i.DaysToEmpty ?? int.MaxValue)
            .ToList();

        var dto = new RestockForecastDto(
            ordered,
            needs,
            basket,
            currency,
            LeadTimeDays,
            MinOrderValue,
            basket >= MinOrderValue);

        return Result<RestockForecastDto>.Success(dto);
    }

    private static (string, string, string) Key(CoffeeProduct p) =>
        (p.Name.ToLowerInvariant(), p.Brand.ToLowerInvariant(), p.Type.ToString().ToLowerInvariant());
}
