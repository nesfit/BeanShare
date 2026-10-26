using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Consumption.Commands;

public sealed class DeleteConsumptionCommandHandler
    : IRequestHandler<DeleteConsumptionCommand, Result<bool>>
{
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IClock _clock;

    public DeleteConsumptionCommandHandler(
        IConsumptionRepository consumptionRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ICoffeeStockRepository coffeeStockRepository,
        IClock clock)
    {
        _consumptionRepository = consumptionRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _coffeeStockRepository = coffeeStockRepository;
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

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(entry.SpaceId, cancellationToken);
        if (coffeeStock != null)
        {
            try
            {
                // Give the grams back to the exact bag the cup drew from (product-level fallback
                // for legacy entries never stamped with a bag). A write-off share also leaves the bag's
                // write-off counter, so a later "Undo empty" restores only what is still written off (Z2, B25).
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

        return Result<bool>.Success(true);
    }
}
