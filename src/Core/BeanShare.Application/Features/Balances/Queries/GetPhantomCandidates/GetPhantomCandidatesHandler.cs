using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Balances.Queries.GetPhantomCandidates;

public sealed class GetPhantomCandidatesHandler : IRequestHandler<GetPhantomCandidatesQuery, Result<PhantomCandidatesDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IUserService _userService;

    public GetPhantomCandidatesHandler(
        ISpaceRepository spaceRepository,
        IConsumptionRepository consumptionRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ICoffeeStockRepository coffeeStockRepository,
        IUserService userService)
    {
        _spaceRepository = spaceRepository;
        _consumptionRepository = consumptionRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _userService = userService;
    }

    public async Task<Result<PhantomCandidatesDto>> Handle(GetPhantomCandidatesQuery query, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(query.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result<PhantomCandidatesDto>.Failure(Error.SpaceNotFound(query.SpaceId));

        if (space.LedgerStartDate is not { } ledgerStart)
            return Result<PhantomCandidatesDto>.Success(new PhantomCandidatesDto(null, query.IncludeAllBagLess, []));

        var rows = await _consumptionRepository.GetBySpecAsync(
            new PredicateSpec<ConsumptionEntry>(c => c.SpaceId == spaceId && c.ConsumedAt >= ledgerStart && (c.IsWriteOff || c.StockLotId == null)),
            cancellationToken);

        var periods = (await _billingPeriodRepository.GetBySpaceIdAsync(spaceId, cancellationToken)).ToDictionary(p => p.Id);
        var bags = (await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken))?.Purchases.ToDictionary(p => p.Id)
            ?? [];

        var candidates = rows
            .Where(r => r.BillingPeriodId is not { } pid || !periods.TryGetValue(pid, out var p) || p.State != BillingState.Settled)
            .Where(r => query.IncludeAllBagLess || r.IsWriteOff || LooksLikeSplit(r))
            .ToList();

        var users = await _userService.GetByIdsAsync(candidates.Select(r => r.UserId).Distinct(), cancellationToken);
        var names = users.ToDictionary(u => u.Id, u => u.Name);

        var events = candidates
            .GroupBy(r => (
                Product: r.Product.DisplayName,
                Minute: new DateTime(r.ConsumedAt.Year, r.ConsumedAt.Month, r.ConsumedAt.Day, r.ConsumedAt.Hour, r.ConsumedAt.Minute, 0, r.ConsumedAt.Kind),
                r.PresetName,
                r.StockLotId))
            .Select(g =>
            {
                var first = g.First();
                var period = first.BillingPeriodId is { } pid && periods.TryGetValue(pid, out var p) ? p : null;
                var state = period?.State;
                return new PhantomEventDto(
                    g.Key.Minute,
                    g.Key.Product,
                    g.Key.StockLotId,
                    g.Key.StockLotId is { } lot && bags.TryGetValue(lot, out var bag) ? bag.Label : null,
                    g.Key.PresetName,
                    g.Any(r => r.IsWriteOff),
                    g.All(LooksLikeSplit),
                    period?.Name,
                    state?.ToString() ?? "Unassigned",
                    state is null or BillingState.Draft or BillingState.Open,
                    g.Sum(r => r.Quantity.Grams),
                    g.Sum(r => r.CostAmount ?? 0m),
                    g.OrderBy(r => names.GetValueOrDefault(r.UserId, ""))
                        .Select(r => new PhantomRowDto(r.Id.Value, r.UserId.Value, names.GetValueOrDefault(r.UserId, "Unknown"), r.Quantity.Grams, r.CostAmount))
                        .ToList());
            })
            .OrderBy(e => e.At)
            .ThenBy(e => e.Product, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<PhantomCandidatesDto>.Success(new PhantomCandidatesDto(ledgerStart, query.IncludeAllBagLess, events));
    }

    /// <summary>The old product-level split: no bag, no preset id, but a preset name.</summary>
    private static bool LooksLikeSplit(ConsumptionEntry row) =>
        row.StockLotId is null && row.PresetId is null && !string.IsNullOrWhiteSpace(row.PresetName);
}
