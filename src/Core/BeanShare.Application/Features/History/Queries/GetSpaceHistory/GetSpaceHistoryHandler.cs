using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.History.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.History.Queries.GetSpaceHistory;

public sealed class GetSpaceHistoryHandler : IRequestHandler<GetSpaceHistoryQuery, Result<SpaceHistoryDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IHistoryImportRepository _historyImportRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;

    public GetSpaceHistoryHandler(
        ISpaceRepository spaceRepository,
        IHistoryImportRepository historyImportRepository,
        IBillingPeriodRepository billingPeriodRepository,
        IConsumptionRepository consumptionRepository,
        ICoffeeStockRepository coffeeStockRepository,
        IUserService userService,
        IUserContext userContext)
    {
        _spaceRepository = spaceRepository;
        _historyImportRepository = historyImportRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _consumptionRepository = consumptionRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _userService = userService;
        _userContext = userContext;
    }

    public async Task<Result<SpaceHistoryDto>> Handle(GetSpaceHistoryQuery query, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(query.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result<SpaceHistoryDto>.Failure(Error.SpaceNotFound(query.SpaceId));

        var imports = await _historyImportRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        var periods = (await _billingPeriodRepository.GetBySpaceIdAsync(spaceId, cancellationToken))
            .Where(p => p.IsHistorical)
            .OrderBy(p => p.StartDate)
            .ToList();
        var historyEnd = periods.Count > 0 ? periods.Max(p => p.EndDate) : (DateTime?)null;

        // Live statistics continue after the last historical period, so nothing is counted twice.
        var liveCups = historyEnd is { } end
            ? (await _consumptionRepository.GetBySpaceIdAsync(spaceId, cancellationToken))
                .Where(c => !c.IsWriteOff && c.ConsumedAt > end)
                .ToList()
            : [];
        var stock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        var livePurchases = historyEnd is { } purchasesAfter
            ? (stock?.Purchases ?? []).Where(p => p.PurchasedAt > purchasesAfter).ToList()
            : [];

        var userIds = imports.SelectMany(i => i.CupCounts.Select(c => c.UserId))
            .Concat(imports.SelectMany(i => i.Purchases.Where(p => p.PaidByUserId.HasValue).Select(p => p.PaidByUserId!.Value)))
            .Concat(imports.Select(i => i.ImportedBy))
            .Concat(liveCups.Select(c => c.UserId))
            .Distinct()
            .ToList();
        var names = (await _userService.GetByIdsAsync(userIds, cancellationToken)).ToDictionary(u => u.Id, u => u.Name);
        string NameOf(UserId id) => names.TryGetValue(id, out var name) ? name : "Unknown";

        var periodDtos = periods
            .Select(p =>
            {
                var import = imports.FirstOrDefault(i => i.Id == p.HistoryImportId);
                var cups = import?.CupCounts.Where(c => c.BillingPeriodId == p.Id)
                    .Select(c => new HistoricalCupsDto(c.UserId.Value, NameOf(c.UserId), c.Cups))
                    .OrderByDescending(c => c.Cups)
                    .ToList() ?? [];
                var purchases = import?.Purchases.Where(x => x.BillingPeriodId == p.Id)
                    .Select(x => new HistoricalPurchaseDto(
                        x.PurchasedAt,
                        x.PaidByUserId?.Value,
                        x.PaidByUserId is { } payer ? NameOf(payer) : null,
                        x.Description,
                        x.Grams,
                        x.Cost))
                    .ToList() ?? [];
                return new HistoricalPeriodDto(p.Id.Value, p.HistoryImportId?.Value ?? Guid.Empty, p.Name, p.StartDate, p.EndDate, cups, purchases);
            })
            .ToList();

        var historicalByUser = imports.SelectMany(i => i.CupCounts)
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Cups));
        var liveByUser = liveCups.GroupBy(c => c.UserId).ToDictionary(g => g.Key, g => g.Count());
        var memberCups = historicalByUser.Keys.Concat(liveByUser.Keys)
            .Distinct()
            .Select(u => new HistoryMemberCupsDto(u.Value, NameOf(u), historicalByUser.GetValueOrDefault(u), liveByUser.GetValueOrDefault(u)))
            .OrderByDescending(m => m.TotalCups)
            .ThenBy(m => m.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var periodEnds = periods.ToDictionary(p => p.Id, p => p.EndDate);
        var yearRows = imports.SelectMany(i => i.Purchases)
            .Select(p => (Year: (p.PurchasedAt ?? periodEnds.GetValueOrDefault(p.BillingPeriodId)).Year, p.Cost, Grams: p.Grams ?? 0m))
            .Concat(livePurchases
                .Where(p => p.Cost.Currency.Code == space.Currency.Code)
                .Select(p => (Year: p.PurchasedAt.Year, Cost: p.Cost.Amount, Grams: p.Quantity.Grams)))
            .GroupBy(r => r.Year)
            .Select(g => new HistoryYearDto(g.Key, g.Sum(r => r.Cost), g.Sum(r => r.Grams), g.Count()))
            .OrderBy(y => y.Year)
            .ToList();

        var importDtos = imports
            .Select(i => new HistoryImportSummaryDto(
                i.Id.Value,
                i.ImportedAt,
                NameOf(i.ImportedBy),
                i.Note,
                periods.Count(p => p.HistoryImportId == i.Id),
                i.CupCounts.Sum(c => c.Cups),
                i.Purchases.Sum(p => p.Cost)))
            .ToList();

        return Result<SpaceHistoryDto>.Success(new SpaceHistoryDto(
            space.Id.Value,
            space.Currency.Code,
            space.IsAdmin(_userContext.CurrentUserId),
            historyEnd,
            importDtos,
            periodDtos,
            memberCups,
            yearRows));
    }
}
