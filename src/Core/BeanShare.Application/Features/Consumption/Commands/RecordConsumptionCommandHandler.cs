using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Consumption.Dtos;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Consumption.Commands;
public sealed class RecordConsumptionCommandHandler : IRequestHandler<RecordConsumptionCommand, Result<ConsumptionEntryDto>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;

    public RecordConsumptionCommandHandler(
        ICoffeeStockRepository coffeeStockRepository,
        IConsumptionRepository consumptionRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext,
        IClock clock,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _consumptionRepository = consumptionRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<ConsumptionEntryDto>> Handle(RecordConsumptionCommand request, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(request.SpaceId);

        // Always loaded: the ledger start date gates every cup (DS-20).
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space == null)
            return Result<ConsumptionEntryDto>.Failure(Error.SpaceNotFound(request.SpaceId));

        UserId targetUserId;
        if (request.ForUserId.HasValue)
        {
            var forUser = new UserId(request.ForUserId.Value);
            if (!space.HasMember(forUser))
                return Result<ConsumptionEntryDto>.Failure(Error.MemberNotFound(request.ForUserId.Value, request.SpaceId));
            if (await _userRepository.IsMergedAsync(forUser, cancellationToken))
                return Result<ConsumptionEntryDto>.Failure(Error.MemberMerged());

            targetUserId = forUser;
        }
        else
        {
            targetUserId = _userContext.CurrentUserId;
        }

        // Checked here as well as by the pipeline, which does not run again when a lost race is retried: a cup must not
        // land on someone who left the space meanwhile.
        if (!space.HasMember(targetUserId))
            return Result<ConsumptionEntryDto>.Failure(Error.MemberNotFound(targetUserId.Value, request.SpaceId));

        if (!Enum.TryParse<Domain.ValueObjects.CoffeeType>(request.ProductType, true, out var coffeeType))
        {
            return Result<ConsumptionEntryDto>.Failure(Error.InvalidCoffeeType());
        }

        var product = CoffeeProduct.Create(request.ProductName, request.ProductBrand, coffeeType);
        var quantity = Weight.FromGrams(request.QuantityGrams);
        var consumedAt = request.ConsumedAt ?? _clock.UtcNow;

        if (consumedAt > _clock.UtcNow.AddMinutes(5))
        {
            return Result<ConsumptionEntryDto>.Failure(Error.InvalidConsumptionTime());
        }

        if (space.LedgerStartDate is { } ledgerStart && consumedAt < ledgerStart)
        {
            return Result<ConsumptionEntryDto>.Failure(Error.BeforeLedgerStart(ledgerStart));
        }

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock == null)
        {
            return Result<ConsumptionEntryDto>.Failure(Error.StockNotFound(request.SpaceId));
        }

        try
        {
            var allocation = coffeeStock.ConsumeStock(product, quantity, _clock, request.StockLotId);
            await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);

            var remainingStock = coffeeStock.GetCurrentStock(product);

            var consumptionEntry = ConsumptionEntry.Create(
                spaceId,
                targetUserId,
                product,
                quantity,
                consumedAt,
                _clock,
                presetId: null,
                presetName: request.PresetName,
                stockLotId: allocation.LotId,
                cost: allocation.Cost,
                unitCostPerGram: allocation.UnitCostPerGram);

            await _consumptionRepository.AddAsync(consumptionEntry, cancellationToken);

            // A cup moves its drinker's balance, so it shares the space's ledger token with Leave and Remove (and with
            // generation): a cup racing a departure makes one of them retry or fail, instead of leaving an ex-member owing it.
            space.BumpLedgerVersion();
            await _spaceRepository.UpdateAsync(space, cancellationToken);

            var dto = new ConsumptionEntryDto
            {
                Id = consumptionEntry.Id.Value,
                SpaceId = consumptionEntry.SpaceId.Value,
                UserId = consumptionEntry.UserId.Value,
                ProductName = consumptionEntry.Product.Name,
                ProductBrand = consumptionEntry.Product.Brand,
                ProductType = consumptionEntry.Product.Type.ToString(),
                QuantityGrams = consumptionEntry.Quantity.Grams,
                RemainingGrams = remainingStock.Grams,
                ConsumedAt = consumptionEntry.ConsumedAt,
                CreatedAt = consumptionEntry.CreatedAt,
                PresetName = request.PresetName,
                CostAmount = consumptionEntry.Cost?.Amount,
                CostCurrency = consumptionEntry.Cost?.Currency.Code
            };

            return Result<ConsumptionEntryDto>.Success(dto);
        }
        catch (ProductNotFoundException)
        {
            return Result<ConsumptionEntryDto>.Failure(Error.ProductNotFoundInStock());
        }
        catch (InsufficientStockException ex)
        {
            return Result<ConsumptionEntryDto>.Failure(Error.InsufficientStock(ex.Message));
        }
    }
}