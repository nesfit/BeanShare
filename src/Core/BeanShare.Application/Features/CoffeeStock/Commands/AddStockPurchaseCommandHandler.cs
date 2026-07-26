using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.CoffeeStock.Dtos;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MapsterMapper;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Commands;
public sealed class AddStockPurchaseCommandHandler : IRequestHandler<AddStockPurchaseCommand, Result<StockPurchaseDto>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IMapper _mapper;

    public AddStockPurchaseCommandHandler(
        ICoffeeStockRepository coffeeStockRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext,
        IClock clock,
        IMapper mapper)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
        _clock = clock;
        _mapper = mapper;
    }

    public async Task<Result<StockPurchaseDto>> Handle(AddStockPurchaseCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var spaceSpec = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);

        if (space == null)
        {
            return Result<StockPurchaseDto>.Failure(Error.SpaceNotFound(command.SpaceId));
        }

        var currentUserId = _userContext.CurrentUserId;
        if (!space.IsAdmin(currentUserId))
        {
            return Result<StockPurchaseDto>.Failure(Error.InsufficientSpacePrivileges("manage coffee stock"));
        }

        if (!string.Equals(space.Currency.Code, command.CostCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return Result<StockPurchaseDto>.Failure(
                Error.CurrencyConflict(space.Currency.Code, command.CostCurrency));
        }

        try
        {
            var product = CoffeeProduct.Create(
                command.ProductName,
                command.ProductBrand,
                Enum.Parse<Domain.ValueObjects.CoffeeType>(command.ProductType));

            var quantity = Weight.FromGrams(command.QuantityGrams);
            var cost = Money.Create(command.CostAmount, command.CostCurrency);

            var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
            bool isNewStock = false;

            if (coffeeStock == null)
            {
                coffeeStock = Domain.Aggregates.CoffeeStock.CoffeeStock.Create(spaceId, _clock);
                await _coffeeStockRepository.AddAsync(coffeeStock, cancellationToken);
                isNewStock = true;
            }
            else
            {
                var existingCurrency = coffeeStock.Purchases
                    .Select(p => p.Cost.Currency)
                    .FirstOrDefault(c => c != command.CostCurrency);
                if (existingCurrency is not null)
                {
                    return Result<StockPurchaseDto>.Failure(
                        Error.CurrencyConflict(existingCurrency, command.CostCurrency));
                }
            }

            coffeeStock.AddPurchase(
                product,
                quantity,
                cost,
                command.Vendor,
                currentUserId,
                command.PurchasedAt,
                _clock,
                command.BagCount,
                command.Barcode);

            if (!isNewStock)
            {
                await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);
            }

            var latestPurchase = coffeeStock.Purchases.OrderByDescending(p => p.CreatedAt).First();
            var dto = _mapper.Map<StockPurchaseDto>(latestPurchase);

            return Result<StockPurchaseDto>.Success(dto);
        }
        catch (ArgumentException ex)
        {
            return Result<StockPurchaseDto>.Failure(Error.ValidationFailure(nameof(AddStockPurchaseCommand), ex.Message));
        }
    }
}