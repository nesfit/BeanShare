using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

public sealed class OpenBagCommandHandler : IRequestHandler<OpenBagCommand, Result<bool>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public OpenBagCommandHandler(
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

    public async Task<Result<bool>> Handle(OpenBagCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space == null)
            return Result<bool>.Failure(Error.SpaceNotFound(command.SpaceId));

        if (!space.IsAdmin(_userContext.CurrentUserId))
            return Result<bool>.Failure(Error.InsufficientSpacePrivileges("open a bag"));

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock == null)
            return Result<bool>.Failure(Error.StockNotFound(command.SpaceId));

        try
        {
            coffeeStock.OpenBag(command.LotId, _clock);
            await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);
            return Result<bool>.Success(true);
        }
        catch (DomainException ex)
        {
            return Result<bool>.Failure(Error.DomainError(ex.Message));
        }
    }
}
