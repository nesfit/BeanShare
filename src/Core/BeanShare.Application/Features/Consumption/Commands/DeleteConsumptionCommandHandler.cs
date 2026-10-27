using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Consumption.Commands;

public sealed class DeleteConsumptionCommandHandler
    : IRequestHandler<DeleteConsumptionCommand, Result<bool>>
{
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IClock _clock;

    public DeleteConsumptionCommandHandler(
        IConsumptionRepository consumptionRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ICoffeeStockRepository coffeeStockRepository,
        ISpaceRepository spaceRepository,
        IClock clock)
    {
        _consumptionRepository = consumptionRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _spaceRepository = spaceRepository;
        _clock = clock;
    }

    public async Task<Result<bool>> Handle(
        DeleteConsumptionCommand request,
        CancellationToken cancellationToken)
    {
        var entryId = new ConsumptionEntryId(request.Id);
        var entry = await _consumptionRepository.GetByIdAsync(entryId, cancellationToken);

        if (entry == null)
            return Result<bool>.Failure(
                Error.NotFound("ConsumptionEntry", $"Consumption entry {request.Id} not found"));

        if (entry.SpaceId.Value != request.SpaceId)
            return Result<bool>.Failure(Error.InsufficientSpacePrivileges("delete this consumption"));

        if (entry.BillingPeriodId != null)
        {
            var billingPeriod = await _billingPeriodRepository.GetByIdAsync(entry.BillingPeriodId.Value, cancellationToken);
            if (billingPeriod != null && billingPeriod.State != BillingState.Draft && billingPeriod.State != BillingState.Open)
                return Result<bool>.Failure(
                    Error.InvalidBillingPeriodState("delete consumption in", billingPeriod.State.ToString()));
        }

        // A cup before the ledger start is never billed; deleting it would only hand its grams back to the bag.
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(entry.SpaceId), cancellationToken);
        if (space?.LedgerStartDate is { } ledgerStart && entry.ConsumedAt < ledgerStart)
            return Result<bool>.Failure(Error.BeforeLedgerStart(ledgerStart));

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(entry.SpaceId, cancellationToken);
        if (coffeeStock != null)
        {
            try
            {
                // Give the grams back to the exact bag the cup drew from (product-level fallback
                // for legacy entries never stamped with a bag). A write-off share also leaves the bag's
                // write-off counter, so a later "Undo empty" restores only what is still written off.
                if (entry.StockLotId.HasValue)
                    coffeeStock.RestoreStockToLot(entry.StockLotId.Value, entry.Quantity, _clock, writeOff: entry.IsWriteOff);
                else
                    coffeeStock.RestoreStock(entry.Product, entry.Quantity, _clock);
                await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);
            }
            catch (DomainException)
            {
                // Product may no longer exist in stock (e.g. archived or renamed).
                // Proceed with consumption deletion regardless.
            }
        }

        await _consumptionRepository.RemoveAsync(entry, cancellationToken);
        // Changes the drinker's balance, so it shares the space's ledger token with Leave, Remove and generation. A
        // conflict on it is not retried: the edit or delete may have lost to a change of the cup itself.
        if (space is not null)
        {
            space.BumpLedgerVersion();
            await _spaceRepository.UpdateAsync(space, cancellationToken);
        }

        return Result<bool>.Success(true);
    }
}
