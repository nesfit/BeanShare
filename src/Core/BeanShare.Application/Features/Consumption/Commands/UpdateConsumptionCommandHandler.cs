using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Consumption.Dtos;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Consumption.Commands;

public sealed class UpdateConsumptionCommandHandler
    : IRequestHandler<UpdateConsumptionCommand, Result<ConsumptionEntryDto>>
{
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IClock _clock;

    public UpdateConsumptionCommandHandler(
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

    public async Task<Result<ConsumptionEntryDto>> Handle(
        UpdateConsumptionCommand request,
        CancellationToken cancellationToken)
    {
        var entryId = new ConsumptionEntryId(request.Id);
        var entry = await _consumptionRepository.GetByIdAsync(entryId, cancellationToken);

        if (entry == null)
            return Result<ConsumptionEntryDto>.Failure(
                Error.NotFound("ConsumptionEntry", $"Consumption entry {request.Id} not found"));

        if (entry.SpaceId.Value != request.SpaceId)
            return Result<ConsumptionEntryDto>.Failure(Error.InsufficientSpacePrivileges("edit this consumption"));

        Domain.Aggregates.BillingPeriod.BillingPeriod? stampedPeriod = null;
        if (entry.BillingPeriodId != null)
        {
            stampedPeriod = await _billingPeriodRepository.GetByIdAsync(entry.BillingPeriodId.Value, cancellationToken);
            if (stampedPeriod != null && stampedPeriod.State != BillingState.Draft && stampedPeriod.State != BillingState.Open)
                return Result<ConsumptionEntryDto>.Failure(
                    Error.InvalidBillingPeriodState("edit consumption in", stampedPeriod.State.ToString()));
        }

        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(entry.SpaceId), cancellationToken);
        // Neither a cup before the ledger start nor a re-date across it: a cup before C is never billed (B22).
        var ledgerStart = space?.LedgerStartDate;
        if (ledgerStart is { } c && entry.ConsumedAt < c)
            return Result<ConsumptionEntryDto>.Failure(Error.BeforeLedgerStart(c));

        // Its edit path would re-draw the share as an ordinary cup (H8). Checked after the refusals that apply to a
        // delete as well, so "delete it instead" is only said when a delete would go through.
        if (entry.IsWriteOff)
            return Result<ConsumptionEntryDto>.Failure(Error.ValidationFailure(
                "Id", "A write-off share cannot be edited; delete it instead"));

        if (ledgerStart is { } start && request.ConsumedAt < start)
            return Result<ConsumptionEntryDto>.Failure(Error.BeforeLedgerStart(start));

        if (!Enum.TryParse<CoffeeType>(request.ProductType, ignoreCase: true, out var coffeeType))
            return Result<ConsumptionEntryDto>.Failure(Error.InvalidCoffeeType());

        var product = CoffeeProduct.Create(request.ProductName.Trim(), request.ProductBrand.Trim(), coffeeType);
        var quantity = Weight.FromGrams(request.QuantityGrams);

        var oldProduct = entry.Product;
        var oldQuantity = entry.Quantity;

        try
        {
            entry.Update(product, quantity, request.ConsumedAt, _clock);
        }
        catch (ArgumentException ex)
        {
            return Result<ConsumptionEntryDto>.Failure(Error.DomainError(ex.Message));
        }

        // A cup re-dated out of its open period loses the stamp; the next close stamps it again (DS-27).
        if (stampedPeriod != null && !stampedPeriod.ContainsDate(entry.ConsumedAt))
            entry.ClearBillingPeriod();

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(entry.SpaceId, cancellationToken);
        if (coffeeStock != null)
        {
            try
            {
                // Reverse the original draw against the exact bag it came from (product-level
                // fallback for legacy entries never stamped with a bag), then re-consume and
                // re-stamp against whichever bag the new amount now draws from.
                if (entry.StockLotId.HasValue)
                    coffeeStock.RestoreStockToLot(entry.StockLotId.Value, oldQuantity, _clock);
                else
                    coffeeStock.RestoreStock(oldProduct, oldQuantity, _clock);

                var allocation = coffeeStock.ConsumeStock(product, quantity, _clock);
                entry.SetAllocation(allocation.LotId, allocation.Cost, allocation.UnitCostPerGram);
                await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);
            }
            catch (DomainException ex)
            {
                return Result<ConsumptionEntryDto>.Failure(Error.DomainError(ex.Message));
            }
        }
        else
        {
            entry.ClearAllocation();
        }

        await _consumptionRepository.UpdateAsync(entry, cancellationToken);

        var dto = new ConsumptionEntryDto
        {
            Id = entry.Id.Value,
            SpaceId = entry.SpaceId.Value,
            UserId = entry.UserId.Value,
            ProductName = entry.Product.Name,
            ProductBrand = entry.Product.Brand,
            ProductType = entry.Product.Type.ToString(),
            QuantityGrams = entry.Quantity.Grams,
            RemainingGrams = 0,
            ConsumedAt = entry.ConsumedAt,
            CreatedAt = entry.CreatedAt,
            CanEdit = true,
            CanDelete = true,
            PresetId = entry.PresetId?.Value,
            PresetName = entry.PresetName
        };

        return Result<ConsumptionEntryDto>.Success(dto);
    }
}
