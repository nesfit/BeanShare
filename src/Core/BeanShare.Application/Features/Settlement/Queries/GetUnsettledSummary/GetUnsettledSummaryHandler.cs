using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Settlement.Queries.GetUnsettledSummary;

public sealed class GetUnsettledSummaryHandler
    : IRequestHandler<GetUnsettledSummaryQuery, Result<UnsettledSummaryResult>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;

    public GetUnsettledSummaryHandler(
        ISpaceRepository spaceRepository,
        IBillingPeriodRepository billingPeriodRepository,
        IConsumptionRepository consumptionRepository,
        ICoffeeStockRepository coffeeStockRepository,
        IUserService userService,
        IUserContext userContext)
    {
        _spaceRepository = spaceRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _consumptionRepository = consumptionRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _userService = userService;
        _userContext = userContext;
    }

    public async Task<Result<UnsettledSummaryResult>> Handle(
        GetUnsettledSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;

        IReadOnlyList<Domain.Aggregates.Space.Space> spaces;
        if (request.SpaceId.HasValue)
        {
            var space = await _spaceRepository.GetByIdAsync(new SpaceId(request.SpaceId.Value), cancellationToken);
            if (space is null)
            {
                return Result<UnsettledSummaryResult>.Failure(Error.SpaceNotFound(request.SpaceId.Value));
            }

            if (!space.HasMember(currentUserId))
            {
                return Result<UnsettledSummaryResult>.Failure(
                    Error.InsufficientSpacePrivileges("view unsettled consumption"));
            }

            spaces = [space];
        }
        else
        {
            spaces = await _spaceRepository.GetBySpecAsync(
                new SpacesWithUserMembershipSpecification(currentUserId), cancellationToken);
        }

        var summaries = new List<UnsettledSpaceSummaryDto>(spaces.Count);
        foreach (var space in spaces)
        {
            summaries.Add(await BuildSummaryAsync(space, currentUserId, cancellationToken));
        }

        return Result<UnsettledSummaryResult>.Success(new UnsettledSummaryResult(summaries));
    }

    private async Task<UnsettledSpaceSummaryDto> BuildSummaryAsync(
        Domain.Aggregates.Space.Space space,
        UserId currentUserId,
        CancellationToken cancellationToken)
    {
        var periods = await _billingPeriodRepository.GetBySpaceIdAsync(space.Id, cancellationToken);
        var settledPeriodIds = periods
            .Where(p => p.State == BillingState.Settled)
            .Select(p => p.Id)
            .ToHashSet();

        var lastSettled = periods
            .Where(p => p.State == BillingState.Settled)
            .OrderByDescending(p => p.EndDate)
            .FirstOrDefault();
        var currentPeriod = periods.FirstOrDefault(p => p.State == BillingState.Open);

        // The settled watermark, pulled back to the start of any still-unsettled period that
        // predates it (an admin can close February, then close *and* settle March).
        var notBefore = lastSettled?.EndDate;
        if (notBefore.HasValue)
        {
            var earliestUnsettledStart = periods
                .Where(p => p.State != BillingState.Settled)
                .Select(p => p.StartDate)
                .DefaultIfEmpty(notBefore.Value)
                .Min();

            if (earliestUnsettledStart < notBefore.Value)
            {
                notBefore = earliestUnsettledStart;
            }
        }

        var candidates = await _consumptionRepository.GetBySpecAsync(
            new UnsettledConsumptionsSpecification(space.Id, notBefore), cancellationToken);

        var unsettled = candidates
            .Where(c => c.BillingPeriodId is null || !settledPeriodIds.Contains(c.BillingPeriodId.Value))
            .ToList();

        var currency = space.Currency;
        var stock = await _coffeeStockRepository.GetBySpaceIdAsync(space.Id, cancellationToken);
        var spacePurchases = stock?.Purchases
            .Where(p => p.Cost.Currency.Code == currency.Code)
            .ToList() ?? [];

        // Money paid in is only counted inside the same window as the cups, so the balance
        // answers "since the last settlement" for both sides of the ledger.
        var windowPurchases = notBefore.HasValue
            ? spacePurchases.Where(p => p.PurchasedAt >= notBefore.Value).ToList()
            : spacePurchases;

        var fallbackPerGram = WeightedAveragePerGram(windowPurchases, spacePurchases);

        var paidByUser = windowPurchases
            .GroupBy(p => p.PurchasedBy)
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(p => p.Cost.Amount), currency.DecimalPlaces));

        var byUser = unsettled
            .GroupBy(c => c.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Cups = g.Count(),
                Grams = g.Sum(c => c.Quantity.Grams),
                Cost = Math.Round(g.Sum(c => CupCost(c, currency, fallbackPerGram)), currency.DecimalPlaces)
            })
            .ToList();

        var mine = byUser.FirstOrDefault(u => u.UserId == currentUserId);
        var isAdmin = space.IsAdmin(currentUserId);

        var members = isAdmin
            ? await BuildMemberRowsAsync(
                byUser.Select(u => (u.UserId, u.Cups, u.Grams, u.Cost)).ToList(),
                paidByUser,
                currentUserId,
                cancellationToken)
            : [];

        return new UnsettledSpaceSummaryDto(
            space.Id.Value,
            space.Name,
            currency.Code,
            lastSettled?.EndDate,
            lastSettled?.Name,
            lastSettled?.SettledAt,
            currentPeriod?.Name,
            isAdmin,
            mine?.Cups ?? 0,
            mine?.Grams ?? 0m,
            mine?.Cost ?? 0m,
            paidByUser.TryGetValue(currentUserId, out var myPaid) ? myPaid : 0m,
            unsettled.Count,
            unsettled.Sum(c => c.Quantity.Grams),
            byUser.Sum(u => u.Cost),
            members);
    }

    private async Task<IReadOnlyList<UnsettledMemberSummaryDto>> BuildMemberRowsAsync(
        IReadOnlyList<(UserId UserId, int Cups, decimal Grams, decimal Cost)> consumers,
        IReadOnlyDictionary<UserId, decimal> paidByUser,
        UserId currentUserId,
        CancellationToken cancellationToken)
    {
        // Members who only bought coffee still belong in the breakdown — that is exactly who the
        // rest of the space owes money to.
        var userIds = consumers.Select(c => c.UserId)
            .Concat(paidByUser.Keys)
            .Distinct()
            .ToList();

        if (userIds.Count == 0)
        {
            return [];
        }

        var users = await _userService.GetByIdsAsync(userIds, cancellationToken);
        var names = users.ToDictionary(u => u.Id, u => u.Name);
        var consumedByUser = consumers.ToDictionary(c => c.UserId);

        return userIds
            .Select(id =>
            {
                consumedByUser.TryGetValue(id, out var consumed);
                return new UnsettledMemberSummaryDto(
                    id.Value,
                    names.TryGetValue(id, out var name) ? name : $"User {id.Value}",
                    consumed.Cups,
                    consumed.Grams,
                    consumed.Cost,
                    paidByUser.TryGetValue(id, out var paid) ? paid : 0m,
                    id == currentUserId);
            })
            .OrderByDescending(m => m.Cost)
            .ThenBy(m => m.UserName)
            .ToList();
    }

    private static decimal CupCost(ConsumptionEntry entry, Currency currency, decimal fallbackPerGram) =>
        entry.Cost != null && entry.Cost.Currency.Code == currency.Code
            ? entry.Cost.Amount
            : fallbackPerGram * entry.Quantity.Grams;

    /// <summary>
    /// Rate used for legacy cups that carry no stamped per-bag cost. Prefers the window's own
    /// purchases and widens to the space's full purchase history only when the window has none,
    /// mirroring <c>GenerateSettlementHandler</c>.
    /// </summary>
    private static decimal WeightedAveragePerGram(
        IReadOnlyList<Purchase> windowPurchases,
        IReadOnlyList<Purchase> allPurchases)
    {
        var source = windowPurchases.Count > 0 ? windowPurchases : allPurchases;
        var grams = source.Sum(p => p.Quantity.Grams);
        return grams > 0 ? source.Sum(p => p.Cost.Amount) / grams : 0m;
    }
}
