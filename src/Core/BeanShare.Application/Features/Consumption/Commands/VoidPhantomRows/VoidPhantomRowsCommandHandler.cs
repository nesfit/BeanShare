using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BeanShare.Application.Features.Consumption.Commands.VoidPhantomRows;

public sealed class VoidPhantomRowsCommandHandler : IRequestHandler<VoidPhantomRowsCommand, Result>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly ILogger<VoidPhantomRowsCommandHandler> _logger;

    public VoidPhantomRowsCommandHandler(
        ISpaceRepository spaceRepository,
        IConsumptionRepository consumptionRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ICoffeeStockRepository coffeeStockRepository,
        IUserContext userContext,
        IClock clock,
        ILogger<VoidPhantomRowsCommandHandler> logger)
    {
        _spaceRepository = spaceRepository;
        _consumptionRepository = consumptionRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _userContext = userContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> Handle(VoidPhantomRowsCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result.Failure(Error.SpaceNotFound(command.SpaceId));

        // Every row is checked before anything changes: one bad id fails the whole batch.
        var rows = new List<ConsumptionEntry>();
        var periodStates = new Dictionary<BillingPeriodId, BillingState?>();
        foreach (var id in command.ConsumptionIds.Distinct())
        {
            var row = await _consumptionRepository.GetByIdAsync(new ConsumptionEntryId(id), cancellationToken);
            if (row is null || row.SpaceId != spaceId)
                return Result.Failure(Error.NotFound("ConsumptionEntry", $"Consumption entry {id} not found"));

            if (!row.IsWriteOff && row.StockLotId is not null)
                return Result.Failure(Error.NotVoidable($"The cup of {row.ConsumedAt:yyyy-MM-dd HH:mm}"));

            if (row.BillingPeriodId is { } periodId)
            {
                if (!periodStates.TryGetValue(periodId, out var state))
                {
                    state = (await _billingPeriodRepository.GetByIdAsync(periodId, cancellationToken))?.State;
                    periodStates[periodId] = state;
                }

                // Also refuses a row already included in a settlement.
                if (state is { } s && s != BillingState.Draft && s != BillingState.Open)
                    return Result.Failure(Error.InvalidBillingPeriodState("void a row in", s.ToString()));
            }

            rows.Add(row);
        }

        var stock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        var perBag = new Dictionary<Guid, decimal>();
        foreach (var share in rows.Where(r => r.IsWriteOff && r.StockLotId is not null))
        {
            var lotId = share.StockLotId!.Value;
            if (stock?.Purchases.Any(p => p.Id == lotId) != true)
                continue;

            stock.VoidWriteOffOnLot(lotId, share.Quantity, _clock);
            perBag[lotId] = perBag.GetValueOrDefault(lotId) + share.Quantity.Grams;
        }

        if (stock is not null && perBag.Count > 0)
            await _coffeeStockRepository.UpdateAsync(stock, cancellationToken);

        foreach (var row in rows)
            await _consumptionRepository.RemoveAsync(row, cancellationToken);

        // A ledger input changed: a racing generation conflicts.
        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);

        var perProduct = rows
            .GroupBy(r => r.Product.DisplayName)
            .Select(g => $"{g.Key}: {g.Sum(r => r.Quantity.Grams):0.#} g")
            .ToList();
        _logger.LogInformation(
            "Admin {AdminId} voided {Count} phantom rows in space {SpaceId}. Reason: {Reason}. Rows: {RowIds}. Per product: {PerProduct}. Per bag: {PerBag}",
            _userContext.CurrentUserId,
            rows.Count,
            spaceId,
            command.Reason.Trim(),
            string.Join(", ", rows.Select(r => r.Id.Value)),
            string.Join("; ", perProduct),
            string.Join("; ", perBag.Select(b => $"{b.Key}: {b.Value:0.#} g")));

        return Result.Success();
    }
}
