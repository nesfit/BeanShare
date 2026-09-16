using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

public sealed class DeleteStockPurchaseCommandHandler : IRequestHandler<DeleteStockPurchaseCommand, Result>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public DeleteStockPurchaseCommandHandler(
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

    public async Task<Result> Handle(DeleteStockPurchaseCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var spaceSpec = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);

        if (space == null)
            return Result.Failure(Error.SpaceNotFound(command.SpaceId));

        var currentUserId = _userContext.CurrentUserId;
        if (!space.IsAdmin(currentUserId))
            return Result.Failure(Error.InsufficientSpacePrivileges("delete stock purchases"));

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock == null)
            return Result.Failure(Error.StockNotFound(command.SpaceId));

        try
        {
            coffeeStock.DeletePurchase(command.PurchaseId, _clock);
            await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Domain.Exceptions.DomainException)
        {
            // "Bag not found" and "already consumed from — mark it empty or discard it instead" both
            // surface as a clean error rather than a 500.
            return Result.Failure(Error.DomainError(ex.Message));
        }
    }
}
