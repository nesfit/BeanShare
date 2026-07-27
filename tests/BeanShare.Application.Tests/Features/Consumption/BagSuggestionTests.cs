using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.Consumption.Queries.GetBagSuggestions;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Services;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Consumption;

/// <summary>
/// Which bag the manual log form preselects: open bags first, then whichever the space has been
/// pouring from over its recent cups — with the caller's own cups weighted heavier.
/// </summary>
public sealed class BagSuggestionTests
{
    private readonly ICoffeeStockRepository _coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
    private readonly IConsumptionRepository _consumptionRepository = Substitute.For<IConsumptionRepository>();
    private readonly IUserContext _userContext = Substitute.For<IUserContext>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly GetBagSuggestionsQueryHandler _handler;

    private readonly SpaceId _spaceId = SpaceId.New();
    private readonly UserId _me = UserId.New();
    private readonly UserId _colleague = UserId.New();
    private readonly DateTime _now = new(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);

    public BagSuggestionTests()
    {
        _clock.UtcNow.Returns(_now);
        _userContext.CurrentUserId.Returns(_me);

        _handler = new GetBagSuggestionsQueryHandler(
            _coffeeStockRepository,
            _consumptionRepository,
            _userContext);
    }

    [Fact]
    public async Task Open_Bag_Outranks_A_Sealed_One_Even_With_No_History()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        var ethiopia = AddBag(stock, "Yirgacheffe", "Ethiopia");
        var brazil = AddBag(stock, "Santos", "Brazil");

        stock.OpenBag(brazil.Id, _clock);

        var result = await HandleAsync(stock, []);

        result.SuggestedLotId.Should().Be(brazil.Id);
        result.Bags[0].IsSuggested.Should().BeTrue();
        result.Bags[1].LotId.Should().Be(ethiopia.Id);
        result.Bags[1].IsSuggested.Should().BeFalse();
    }

    [Fact]
    public async Task Among_Open_Bags_The_One_Poured_From_Most_Recently_Wins()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        var ethiopia = AddBag(stock, "Yirgacheffe", "Ethiopia");
        var brazil = AddBag(stock, "Santos", "Brazil");

        stock.OpenBag(ethiopia.Id, _clock);
        stock.OpenBag(brazil.Id, _clock);

        // The office has been drinking the Ethiopia — nobody's cups but theirs.
        var entries = Enumerable.Range(1, 7)
            .Select(i => Cup(ethiopia.Id, _colleague, _now.AddHours(-i)))
            .Concat(Enumerable.Range(8, 3).Select(i => Cup(brazil.Id, _colleague, _now.AddHours(-i))))
            .ToList();

        var result = await HandleAsync(stock, entries);

        result.SuggestedLotId.Should().Be(ethiopia.Id);
        result.Bags[0].RecentCups.Should().Be(7);
        result.Bags[0].MyRecentCups.Should().Be(0);
        result.Bags[0].SuggestionReason.Should().Be("Used for 7 of the last 10 cups");
    }

    [Fact]
    public async Task My_Own_Cups_Count_Double_So_My_Bag_Wins_A_Near_Tie()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        var mine = AddBag(stock, "Yirgacheffe", "Ethiopia");
        var theirs = AddBag(stock, "Santos", "Brazil");

        stock.OpenBag(mine.Id, _clock);
        stock.OpenBag(theirs.Id, _clock);

        // Their bag has more cups overall (4 vs 3), but all three of mine are mine: 3×2 = 6 > 4.
        var entries = Enumerable.Range(1, 3)
            .Select(i => Cup(mine.Id, _me, _now.AddHours(-i)))
            .Concat(Enumerable.Range(4, 4).Select(i => Cup(theirs.Id, _colleague, _now.AddHours(-i))))
            .ToList();

        var result = await HandleAsync(stock, entries);

        result.SuggestedLotId.Should().Be(mine.Id);
        result.Bags[0].RecentCups.Should().Be(3);
        result.Bags[0].MyRecentCups.Should().Be(3);
        result.Bags[0].SuggestionReason.Should().Be("Used for 3 of the last 7 cups (3 of them yours)");
    }

    [Fact]
    public async Task Cups_Outside_The_Window_Do_Not_Vote()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        var stale = AddBag(stock, "Yirgacheffe", "Ethiopia");
        var current = AddBag(stock, "Santos", "Brazil");

        stock.OpenBag(stale.Id, _clock);
        stock.OpenBag(current.Id, _clock);

        // Yesterday's bag dominated, but the last three cups all came from the other one.
        var entries = Enumerable.Range(1, 3)
            .Select(i => Cup(current.Id, _colleague, _now.AddMinutes(-i)))
            .Concat(Enumerable.Range(1, 9).Select(i => Cup(stale.Id, _colleague, _now.AddDays(-1).AddHours(-i))))
            .ToList();

        var result = await HandleAsync(stock, entries, windowSize: 3);

        result.SuggestedLotId.Should().Be(current.Id);
        result.WindowCups.Should().Be(3);
    }

    [Fact]
    public async Task Empty_Bags_Are_Never_Offered()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        var drained = AddBag(stock, "Yirgacheffe", "Ethiopia", grams: 10);
        var spare = AddBag(stock, "Santos", "Brazil");

        stock.OpenBag(drained.Id, _clock);
        stock.ConsumeStock(
            CoffeeProduct.Create("Yirgacheffe", "Ethiopia", CoffeeType.Espresso),
            Weight.FromGrams(10),
            _clock,
            drained.Id);

        var result = await HandleAsync(stock, [Cup(drained.Id, _me, _now.AddMinutes(-5))]);

        result.Bags.Should().ContainSingle();
        result.Bags[0].LotId.Should().Be(spare.Id);
        result.Bags[0].IsSuggested.Should().BeTrue();
    }

    [Fact]
    public async Task With_Nothing_Open_The_Oldest_Bag_Is_Offered_First()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        var older = AddBag(stock, "Yirgacheffe", "Ethiopia", purchasedAt: _now.AddDays(-30));
        AddBag(stock, "Santos", "Brazil", purchasedAt: _now.AddDays(-2));

        var result = await HandleAsync(stock, []);

        result.SuggestedLotId.Should().Be(older.Id);
        result.Bags[0].IsOpen.Should().BeFalse();
        result.Bags[0].SuggestionReason.Should().Be("Nothing is open — this is the oldest bag in stock");
    }

    [Fact]
    public async Task A_Space_Without_Stock_Suggests_Nothing()
    {
        _coffeeStockRepository.GetBySpaceIdAsync(_spaceId, Arg.Any<CancellationToken>())
            .Returns((CoffeeStock?)null);

        var result = await _handler.Handle(new GetBagSuggestionsQuery(_spaceId.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Bags.Should().BeEmpty();
        result.Value.SuggestedLotId.Should().BeNull();
    }

    [Fact]
    public void Legacy_Cups_With_No_Bag_Attribution_Still_Consume_A_Window_Slot()
    {
        var stock = CoffeeStock.Create(_spaceId, _clock);
        var bag = AddBag(stock, "Yirgacheffe", "Ethiopia");
        stock.OpenBag(bag.Id, _clock);

        var entries = new List<ConsumptionEntry>
        {
            Cup(null, _colleague, _now.AddMinutes(-1)),
            Cup(null, _colleague, _now.AddMinutes(-2)),
            Cup(bag.Id, _colleague, _now.AddMinutes(-3))
        };

        var ranked = BagSuggestionPolicy.Rank(stock.Purchases, entries, _me, windowSize: 2);

        ranked.Should().ContainSingle();
        ranked[0].RecentCups.Should().Be(0, because: "the only attributed cup fell outside the 2-cup window");
    }

    private async Task<BagSuggestionsResult> HandleAsync(
        CoffeeStock stock,
        IReadOnlyList<ConsumptionEntry> entries,
        int windowSize = BagSuggestionPolicy.DefaultWindowSize)
    {
        _coffeeStockRepository.GetBySpaceIdAsync(_spaceId, Arg.Any<CancellationToken>()).Returns(stock);
        _consumptionRepository
            .GetRecentBySpaceIdAsync(_spaceId, windowSize, null, Arg.Any<CancellationToken>())
            .Returns(entries
                .OrderByDescending(e => e.ConsumedAt)
                .Take(windowSize)
                .ToList());

        var result = await _handler.Handle(
            new GetBagSuggestionsQuery(_spaceId.Value, windowSize),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private Purchase AddBag(
        CoffeeStock stock,
        string name,
        string brand,
        decimal grams = 1000,
        DateTime? purchasedAt = null)
    {
        var product = CoffeeProduct.Create(name, brand, CoffeeType.Espresso);
        stock.AddPurchase(
            product,
            Weight.FromGrams(grams),
            Money.Create(20, "EUR"),
            "Test Vendor",
            _me,
            purchasedAt ?? _now.AddDays(-7),
            _clock);

        return stock.Purchases.Last();
    }

    private ConsumptionEntry Cup(Guid? lotId, UserId userId, DateTime consumedAt)
        => ConsumptionEntry.Create(
            _spaceId,
            userId,
            CoffeeProduct.Create("Yirgacheffe", "Ethiopia", CoffeeType.Espresso),
            Weight.FromGrams(8),
            consumedAt,
            _clock,
            stockLotId: lotId);
}
