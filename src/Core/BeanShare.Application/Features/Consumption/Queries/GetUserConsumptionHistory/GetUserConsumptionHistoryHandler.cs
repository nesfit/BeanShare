using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Consumption.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Consumption.Queries.GetUserConsumptionHistory;
public sealed class GetUserConsumptionHistoryHandler : IRequestHandler<GetUserConsumptionHistoryQuery, Result<ConsumptionHistoryDto>>
{
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ICostCalculationService _costCalculationService;
    private readonly IUserContext _userContext;

    public GetUserConsumptionHistoryHandler(
        IConsumptionRepository consumptionRepository,
        ISpaceRepository spaceRepository,
        IUserRepository userRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ICostCalculationService costCalculationService,
        IUserContext userContext)
    {
        _consumptionRepository = consumptionRepository;
        _spaceRepository = spaceRepository;
        _userRepository = userRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _costCalculationService = costCalculationService;
        _userContext = userContext;
    }

    public async Task<Result<ConsumptionHistoryDto>> Handle(GetUserConsumptionHistoryQuery query, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;
        var isSystemAdmin = _userContext.Roles.Contains("admin");

        // When an admin requests history for a specific user across all spaces (no SpaceId filter),
        // resolve the spaces that belong to the target user instead of the current user.
        var isAdminGlobalUserQuery = !query.SpaceId.HasValue && query.MemberUserId.HasValue && isSystemAdmin;

        var spacesSpecUserId = isAdminGlobalUserQuery
            ? new UserId(query.MemberUserId!.Value)
            : currentUserId;

        var userSpacesSpec = new SpacesWithUserMembershipSpecification(spacesSpecUserId);
        var userSpaces = await _spaceRepository.GetBySpecAsync(userSpacesSpec, cancellationToken);

        if (!userSpaces.Any())
        {
            return Result<ConsumptionHistoryDto>.Success(new ConsumptionHistoryDto(
                0,
                query.PageNumber,
                query.PageSize,
                new List<ConsumptionHistoryItemDto>(),
                new ConsumptionHistorySummaryDto(0, 0, 0, 0, new Dictionary<string, int>(), new Dictionary<string, decimal>())
            ));
        }

        var spaces = new Dictionary<SpaceId, Domain.Aggregates.Space.Space>();

        var isSpaceWideQuery = query.SpaceId.HasValue && !query.MemberUserId.HasValue;
        var isSpecificMemberQuery = query.SpaceId.HasValue && query.MemberUserId.HasValue;

        var specs = new List<ISpec<Domain.Entities.ConsumptionEntry>>();

        foreach (var space in userSpaces)
        {
            if (query.SpaceId.HasValue && space.Id.Value != query.SpaceId.Value)
            {
                continue;
            }

            spaces[space.Id] = space;

            BillingPeriodId? billingPeriodId = query.BillingPeriodId.HasValue
                ? new BillingPeriodId(query.BillingPeriodId.Value)
                : (BillingPeriodId?)null;

            if (isSpaceWideQuery || isSpecificMemberQuery)
            {
                UserId? targetUserId = query.MemberUserId.HasValue
                    ? new UserId(query.MemberUserId.Value)
                    : null;

                specs.Add(new ConsumptionsBySpaceAndDateRangeSpecification(
                    space.Id, targetUserId, query.StartDate, query.EndDate, billingPeriodId));
            }
            else if (isAdminGlobalUserQuery)
            {
                var targetUserId = new UserId(query.MemberUserId!.Value);
                specs.Add(new ConsumptionsByUserAndDateRangeSpecification(
                    targetUserId, space.Id, query.StartDate, query.EndDate, billingPeriodId));
            }
            else
            {
                specs.Add(new ConsumptionsByUserAndDateRangeSpecification(
                    currentUserId, space.Id, query.StartDate, query.EndDate, billingPeriodId));
            }
        }

        if (query.SpaceId.HasValue && !spaces.Any(s => s.Key.Value == query.SpaceId.Value))
        {
            var requestedSpaceId = new SpaceId(query.SpaceId.Value);
            var spaceByIdSpec = new SpaceByIdSpecification(requestedSpaceId);
            var requestedSpace = await _spaceRepository.GetSingleBySpecAsync(spaceByIdSpec, cancellationToken);

            if (requestedSpace == null)
            {
                return Result<ConsumptionHistoryDto>.Failure(Error.SpaceNotFound(query.SpaceId.Value));
            }

            return Result<ConsumptionHistoryDto>.Failure(Error.InsufficientSpacePrivileges("view consumption history"));
        }

        var skip = (query.PageNumber - 1) * query.PageSize;
        int totalCount;
        List<Domain.Entities.ConsumptionEntry> paginatedConsumptions;

        if (specs.Count == 1)
        {
            var (pagedItems, count) = await _consumptionRepository.GetPagedBySpecAsync(
                specs[0], skip, query.PageSize, cancellationToken);
            totalCount = count;
            paginatedConsumptions = pagedItems.ToList();
        }
        else
        {
 
            var allConsumptions = new List<Domain.Entities.ConsumptionEntry>();
            foreach (var spec in specs)
            {
                var spaceConsumptions = await _consumptionRepository.GetBySpecAsync(spec, cancellationToken);
                allConsumptions.AddRange(spaceConsumptions);
            }

            totalCount = allConsumptions.Count;
            paginatedConsumptions = allConsumptions
                .OrderByDescending(c => c.ConsumedAt)
                .Skip(skip)
                .Take(query.PageSize)
                .ToList();
        }

        var userIds = paginatedConsumptions.Select(c => c.UserId).Distinct().ToList();
        var userNames = new Dictionary<UserId, string>();
        foreach (var uid in userIds)
        {
            var user = await _userRepository.GetByIdAsync(uid, cancellationToken);
            userNames[uid] = user?.Name ?? "Unknown";
        }

        var editablePeriodIds = new HashSet<BillingPeriodId>();
        if (query.SpaceId.HasValue)
        {
            var spaceId = new SpaceId(query.SpaceId.Value);
            var billingPeriods = await _billingPeriodRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
            editablePeriodIds = billingPeriods
                .Where(bp => bp.State == BillingState.Draft || bp.State == BillingState.Open)
                .Select(bp => bp.Id)
                .ToHashSet();
        }
        else
        {
 
            var spaceIdsInResults = paginatedConsumptions.Select(c => c.SpaceId).Distinct().ToList();
            foreach (var sid in spaceIdsInResults)
            {
                var billingPeriods = await _billingPeriodRepository.GetBySpaceIdAsync(sid, cancellationToken);
                foreach (var bp in billingPeriods.Where(bp => bp.State == BillingState.Draft || bp.State == BillingState.Open))
                {
                    editablePeriodIds.Add(bp.Id);
                }
            }
        }

        var items = new List<ConsumptionHistoryItemDto>();
        foreach (var c in paginatedConsumptions)
        {
            var spaceName = spaces.TryGetValue(c.SpaceId, out var space) ? space.Name : "Unknown Space";
            decimal? estimatedCost = null;
            string? currency = null;

            // Prefer the cost stamped on the entry at log time (per-bag, exact). Fall back to
            // the space-wide weighted-average estimate for legacy/unattributed cups (null cost).
            if (c.Cost != null)
            {
                estimatedCost = c.Cost.Amount;
                currency = c.Cost.Currency.Code;
            }
            else
            {
                var cost = await _costCalculationService.CalculateConsumptionCostAsync(c.SpaceId, c.Quantity.Grams, cancellationToken);
                if (cost != null)
                {
                    estimatedCost = cost.Amount;
                    currency = cost.Currency.Code;
                }
            }

            // Cups before the ledger start are never billed, so they cannot be edited either.
            var beforeLedgerStart = space?.LedgerStartDate is { } ledgerStart && c.ConsumedAt < ledgerStart;
            var canEdit = !beforeLedgerStart && (c.BillingPeriodId == null || editablePeriodIds.Contains(c.BillingPeriodId.Value));

            items.Add(new ConsumptionHistoryItemDto(
                c.Id.Value,
                c.SpaceId.Value,
                spaceName,
                c.Product.Name,
                c.Product.Brand,
                c.Product.Type.ToString(),
                c.Quantity.Grams,
                c.ConsumedAt,
                estimatedCost,
                currency,
                c.BillingPeriodId?.Value,
                null,
                c.UserId.Value,
                userNames.TryGetValue(c.UserId, out var userName) ? userName : "Unknown",
                canEdit
            ));
        }

        var allForSummary = new List<Domain.Entities.ConsumptionEntry>();
        foreach (var spec in specs)
        {
            var entries = await _consumptionRepository.GetBySpecAsync(spec, cancellationToken);
            allForSummary.AddRange(entries);
        }

        var totalGrams = allForSummary.Sum(c => c.Quantity.Grams);
        var totalEntries = allForSummary.Count;
        var uniqueDays = allForSummary.Select(c => c.ConsumedAt.Date).Distinct().Count();
        var averagePerDay = uniqueDays > 0 ? totalGrams / uniqueDays : 0;

        var consumptionByType = allForSummary
            .GroupBy(c => c.Product.Type.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var consumptionGramsBySpace = allForSummary
            .GroupBy(c => c.SpaceId)
            .Select(g => new {
                SpaceName = spaces.TryGetValue(g.Key, out var s) ? s.Name : "Unknown",
                TotalGrams = g.Sum(c => c.Quantity.Grams)
            })
            .ToDictionary(x => x.SpaceName, x => x.TotalGrams);

        var summary = new ConsumptionHistorySummaryDto(
            totalGrams,
            totalEntries,
            uniqueDays,
            Math.Round(averagePerDay, 2),
            consumptionByType,
            consumptionGramsBySpace
        );

        var result = new ConsumptionHistoryDto(
            totalCount,
            query.PageNumber,
            query.PageSize,
            items,
            summary
        );

        return Result<ConsumptionHistoryDto>.Success(result);
    }
}
