using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Consumption.Dtos;
using BeanShare.Domain.Services;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Consumption.Queries.GetBagSuggestions;

public sealed class GetBagSuggestionsQueryHandler
    : IRequestHandler<GetBagSuggestionsQuery, Result<BagSuggestionsResult>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly IUserContext _userContext;

    public GetBagSuggestionsQueryHandler(
        ICoffeeStockRepository coffeeStockRepository,
        IConsumptionRepository consumptionRepository,
        IUserContext userContext)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _consumptionRepository = consumptionRepository;
        _userContext = userContext;
    }

    public async Task<Result<BagSuggestionsResult>> Handle(
        GetBagSuggestionsQuery request,
        CancellationToken cancellationToken)
    {
        var windowSize = request.WindowSize > 0
            ? request.WindowSize
            : BagSuggestionPolicy.DefaultWindowSize;

        var spaceId = new SpaceId(request.SpaceId);
        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);

        if (coffeeStock == null)
        {
            return Result<BagSuggestionsResult>.Success(
                new BagSuggestionsResult([], windowSize, 0, null));
        }

        var window = await _consumptionRepository.GetRecentBySpaceIdAsync(
            spaceId, windowSize, forUserId: null, ct: cancellationToken);

        var ranked = BagSuggestionPolicy.Rank(
            coffeeStock.Purchases,
            window,
            _userContext.CurrentUserId,
            windowSize);

        // The product rollup is derived, so its id can only come from the projection itself.
        var stockLevelIds = coffeeStock.StockLevels.ToDictionary(
            sl => ProductKey(sl.Product.Name, sl.Product.Brand, sl.Product.Type),
            sl => sl.Id);

        var openCount = ranked.Count(b => b.Lot.IsOpen);

        var bags = ranked
            .Select(b => new BagSuggestionDto
            {
                LotId = b.Lot.Id,
                Label = b.Lot.Label,
                StockLevelId = stockLevelIds.GetValueOrDefault(
                    ProductKey(b.Lot.Product.Name, b.Lot.Product.Brand, b.Lot.Product.Type)),
                ProductName = b.Lot.Product.Name,
                ProductBrand = b.Lot.Product.Brand,
                ProductType = b.Lot.Product.Type.ToString(),
                ProductDisplayName = b.Lot.Product.DisplayName,
                RemainingGrams = b.Lot.RemainingGrams.Grams,
                Status = b.Lot.Status.ToString(),
                IsOpen = b.Lot.IsOpen,
                OpenedAt = b.Lot.OpenedAt,
                RecentCups = b.RecentCups,
                MyRecentCups = b.MyRecentCups,
                LastUsedAt = b.LastUsedAt,
                IsSuggested = b.IsSuggested,
                SuggestionReason = b.IsSuggested ? DescribeSuggestion(b, window.Count, openCount) : null
            })
            .ToList();

        return Result<BagSuggestionsResult>.Success(new BagSuggestionsResult(
            bags,
            windowSize,
            window.Count,
            bags.FirstOrDefault(b => b.IsSuggested)?.LotId));
    }

    private static string DescribeSuggestion(RankedBag bag, int windowCups, int openCount)
    {
        if (bag.RecentCups > 0)
        {
            var share = $"Used for {bag.RecentCups} of the last {windowCups} cup{(windowCups == 1 ? "" : "s")}";
            return bag.MyRecentCups > 0
                ? $"{share} ({bag.MyRecentCups} of them yours)"
                : share;
        }

        if (bag.Lot.IsOpen)
        {
            return openCount == 1
                ? "The only bag that's open"
                : "Open, and opened most recently";
        }

        return "Nothing is open — this is the oldest bag in stock";
    }

    private static string ProductKey(string name, string brand, CoffeeType type)
        => $"{name.ToLowerInvariant()}|{brand.ToLowerInvariant()}|{(int)type}";
}
