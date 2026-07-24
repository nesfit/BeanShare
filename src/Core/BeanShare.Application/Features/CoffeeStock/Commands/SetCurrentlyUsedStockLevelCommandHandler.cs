using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

public sealed class SetCurrentlyUsedStockLevelCommandHandler : IRequestHandler<SetCurrentlyUsedStockLevelCommand, Result<bool>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public SetCurrentlyUsedStockLevelCommandHandler(
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

    public async Task<Result<bool>> Handle(SetCurrentlyUsedStockLevelCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var spaceSpec = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);

        if (space == null)
        {
            return Result<bool>.Failure(Error.SpaceNotFound(command.SpaceId));
        }

        var currentUserId = _userContext.CurrentUserId;
        if (!space.IsAdmin(currentUserId))
        {
            return Result<bool>.Failure(Error.InsufficientSpacePrivileges("set currently used coffee"));
        }

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock == null)
        {
            return Result<bool>.Failure(Error.StockNotFound(command.SpaceId));
        }

        try
        {
            // "Currently used" is now expressed per bag: opening a bag of the product makes it the
            // one cups draw from. Clearing is a no-op — open bags stay open until emptied.
            if (command.StockLevelId.HasValue)
            {
                var level = coffeeStock.StockLevels.FirstOrDefault(sl => sl.Id == command.StockLevelId.Value);
                if (level == null)
                    return Result<bool>.Failure(Error.StockLevelNotFound(command.StockLevelId.Value));

                var lot = coffeeStock.Purchases
                    .Where(p => !p.IsEmpty
                        && string.Equals(p.Product.Name, level.Product.Name, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(p.Product.Brand, level.Product.Brand, StringComparison.OrdinalIgnoreCase)
                        && p.Product.Type == level.Product.Type)
                    .OrderBy(p => p.PurchasedAt).ThenBy(p => p.CreatedAt)
                    .FirstOrDefault();

                if (lot != null)
                    coffeeStock.OpenBag(lot.Id, _clock);
            }

            await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);
            return Result<bool>.Success(true);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result<bool>.Failure(Error.DomainError(ex.Message));
        }
    }
}
