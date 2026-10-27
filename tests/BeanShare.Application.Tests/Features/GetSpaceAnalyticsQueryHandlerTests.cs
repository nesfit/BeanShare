using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.Analytics.Queries.GetSpaceAnalytics;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace BeanShare.Application.Tests.Features;

public sealed class GetSpaceAnalyticsQueryHandlerTests
{
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ICostCalculationService _costCalculationService;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly GetSpaceAnalyticsQueryHandler _handler;

    private readonly UserId _currentUserId;
    private readonly SpaceId _spaceId;
    private readonly DateTime _now = new DateTime(2025, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    public GetSpaceAnalyticsQueryHandlerTests()
    {
        _consumptionRepository = Substitute.For<IConsumptionRepository>();
        _spaceRepository = Substitute.For<ISpaceRepository>();
        _coffeeStockRepository = Substitute.For<ICoffeeStockRepository>();
        _costCalculationService = Substitute.For<ICostCalculationService>();
        _userService = Substitute.For<IUserService>();
        _userContext = Substitute.For<IUserContext>();
        _clock = Substitute.For<IClock>();

        _currentUserId = new UserId(Guid.NewGuid());
        _spaceId = SpaceId.New();

        _userContext.CurrentUserId.Returns(_currentUserId);
        _clock.UtcNow.Returns(_now);

        _costCalculationService.CalculateTotalCostAsync(Arg.Any<SpaceId>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns((Money?)null);
        _costCalculationService.GetAverageCostPerGramAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>())
            .Returns((Money?)null);
        _coffeeStockRepository.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>())
            .Returns((BeanShare.Domain.Aggregates.CoffeeStock.CoffeeStock?)null);

        _handler = new GetSpaceAnalyticsQueryHandler(
            _consumptionRepository,
            _spaceRepository,
            _coffeeStockRepository,
            _costCalculationService,
            _userService,
            _userContext,
            _clock);
    }

    [Fact]
    public async Task GetSpaceAnalytics_WhenMoreThanTenConsumers_ShouldReturnAllConsumers()
    {
        // Arrange: 12 distinct users, each with one consumption — verifies no hard cap
        var space = CreateSpaceWithCurrentUser();
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var consumptions = CreateConsumptionsForDistinctUsers(12);
        _consumptionRepository.GetBySpaceIdAsync(_spaceId, Arg.Any<CancellationToken>())
            .Returns(consumptions);

        var userIds = consumptions.Select(c => c.UserId).Distinct().ToList();
        var users = userIds.Select((uid, i) => User.CreateWithPassword(
            $"user{i}@test.com", $"User {i}", "hash", _now)).ToList();

        // Match UserId to User by index position
        for (int i = 0; i < userIds.Count; i++)
        {
            var capturedIdx = i;
        }
        _userService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(users);

        var query = new GetSpaceAnalyticsQuery(_spaceId);

        // Act
        var result = await _handler.Handle(query, default);

        // Assert: all 12 consumers are returned, not capped to 5 or 10
        result.TopConsumers.Should().HaveCount(12);
    }

    [Fact]
    public async Task GetSpaceAnalytics_ShouldSortConsumersByCupCountDescendingByDefault()
    {
        // Arrange: 3 users with different cup counts
        var space = CreateSpaceWithCurrentUser();
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var user1 = new UserId(Guid.NewGuid());
        var user2 = new UserId(Guid.NewGuid());
        var user3 = new UserId(Guid.NewGuid());

        var consumptions = new List<ConsumptionEntry>
        {
            CreateConsumption(user2, _now.AddHours(-3)),  // 1 cup
            CreateConsumption(user1, _now.AddHours(-2)),  // 3 cups
            CreateConsumption(user1, _now.AddHours(-4)),
            CreateConsumption(user1, _now.AddHours(-5)),
            CreateConsumption(user3, _now.AddHours(-1)),  // 2 cups
            CreateConsumption(user3, _now.AddHours(-6)),
        };

        _consumptionRepository.GetBySpaceIdAsync(_spaceId, Arg.Any<CancellationToken>())
            .Returns(consumptions);

        var users = new List<User>
        {
            User.CreateWithPassword("u1@test.com", "Alice", "hash", _now),
            User.CreateWithPassword("u2@test.com", "Bob",   "hash", _now),
            User.CreateWithPassword("u3@test.com", "Carol", "hash", _now),
        };
        _userService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(users);

        var query = new GetSpaceAnalyticsQuery(_spaceId);

        // Act
        var result = await _handler.Handle(query, default);

        // Assert: handler returns consumers in descending cup-count order
        result.TopConsumers.Should().HaveCount(3);
        result.TopConsumers[0].CupCount.Should().Be(3);
        result.TopConsumers[1].CupCount.Should().Be(2);
        result.TopConsumers[2].CupCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSpaceAnalytics_WhenSpaceNotFound_ShouldReturnEmptyDto()
    {
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>())
            .Returns((Space?)null);

        var query = new GetSpaceAnalyticsQuery(_spaceId);

        var result = await _handler.Handle(query, default);

        result.TotalMembers.Should().Be(0);
        result.TopConsumers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSpaceAnalytics_WhenCurrentUserNotMember_ShouldReturnEmptyDto()
    {
        // Space created by a different user — current user is not a member
        var otherId = new UserId(Guid.NewGuid());
        var space = Space.Create(SpaceId.New(), "Other Space", Currency.USD, otherId, new InviteCode("XXXXXX"), _clock);

        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>())
            .Returns(space);

        var query = new GetSpaceAnalyticsQuery(_spaceId);

        var result = await _handler.Handle(query, default);

        result.TotalMembers.Should().Be(0);
        result.TopConsumers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSpaceAnalytics_WhenNoConsumptions_ShouldReturnEmptyConsumerList()
    {
        var space = CreateSpaceWithCurrentUser();
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>())
            .Returns(space);

        _consumptionRepository.GetBySpaceIdAsync(_spaceId, Arg.Any<CancellationToken>())
            .Returns(new List<ConsumptionEntry>());

        var query = new GetSpaceAnalyticsQuery(_spaceId);

        var result = await _handler.Handle(query, default);

        result.TopConsumers.Should().BeEmpty();
    }

    // Placeholders, merged ones included, use no app, so the member total leaves them out.
    [Fact]
    public async Task GetSpaceAnalytics_TotalMembers_LeavesOutPlaceholders()
    {
        var space = CreateSpaceWithCurrentUser();
        var member = UserId.New();
        var pete = User.CreatePlaceholder("Old Pete", null, _now);
        var olga = User.CreatePlaceholder("Old Olga", null, _now);
        olga.MarkMergedInto(member);
        space.Join(member, _clock);
        space.AddPlaceholderMember(pete.Id, _clock);
        space.AddPlaceholderMember(olga.Id, _clock);
        _spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>()).Returns(space);
        _consumptionRepository.GetBySpaceIdAsync(_spaceId, Arg.Any<CancellationToken>()).Returns(new List<ConsumptionEntry>());
        _userService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), Arg.Any<CancellationToken>()).Returns(new List<User> { pete, olga });

        var result = await _handler.Handle(new GetSpaceAnalyticsQuery(_spaceId), default);

        result.TotalMembers.Should().Be(2);
    }

    // ---- helpers -------------------------------------------------------

    private Space CreateSpaceWithCurrentUser()
        => Space.Create(_spaceId, "Test Space", Currency.USD, _currentUserId, new InviteCode("TESTAB"), _clock);

    private List<ConsumptionEntry> CreateConsumptionsForDistinctUsers(int userCount)
    {
        var list = new List<ConsumptionEntry>();
        for (int i = 0; i < userCount; i++)
        {
            var uid = new UserId(Guid.NewGuid());
            list.Add(CreateConsumption(uid, _now.AddHours(-i - 1)));
        }
        return list;
    }

    private ConsumptionEntry CreateConsumption(UserId userId, DateTime consumedAt)
    {
        var product = CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso);
        return ConsumptionEntry.Create(_spaceId, userId, product, Weight.FromGrams(18m), consumedAt, _clock);
    }
}
