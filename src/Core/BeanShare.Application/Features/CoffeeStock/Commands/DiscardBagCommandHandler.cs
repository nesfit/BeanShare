using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

public sealed class DiscardBagCommandHandler : IRequestHandler<DiscardBagCommand, Result<DiscardBagResult>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public DiscardBagCommandHandler(
        ICoffeeStockRepository coffeeStockRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext,
        IClock clock)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<DiscardBagResult>> Handle(DiscardBagCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space == null)
            return Result<DiscardBagResult>.Failure(Error.SpaceNotFound(command.SpaceId));

        if (!space.IsAdmin(_userContext.CurrentUserId))
            return Result<DiscardBagResult>.Failure(Error.InsufficientSpacePrivileges("discard a bag"));

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock == null)
            return Result<DiscardBagResult>.Failure(Error.StockNotFound(command.SpaceId));

        var bag = coffeeStock.Purchases.FirstOrDefault(p => p.Id == command.LotId);
        if (bag == null)
            return Result<DiscardBagResult>.Failure(Error.StockLevelNotFound(command.LotId));

        if (bag.IsEmpty || !bag.RemainingGrams.IsPositive)
            return Result<DiscardBagResult>.Failure(Error.NoRemainingStock());

        try
        {
            var discarded = coffeeStock.DiscardBag(bag.Id, _clock);
            await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);

            return Result<DiscardBagResult>.Success(new DiscardBagResult(
                bag.Id,
                bag.Product.DisplayName,
                bag.Label,
                discarded.Grams,
                Math.Round(bag.UnitCostPerGram * discarded.Grams, 2),
                bag.Cost.Currency.Code));
        }
        catch (DomainException ex)
        {
            return Result<DiscardBagResult>.Failure(Error.DomainError(ex.Message));
        }
    }
}
