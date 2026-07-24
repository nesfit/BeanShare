using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

public sealed class ArchiveStockLevelCommandHandler : IRequestHandler<ArchiveStockLevelCommand, Result<bool>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;

    public ArchiveStockLevelCommandHandler(
        ICoffeeStockRepository coffeeStockRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
    }

    public async Task<Result<bool>> Handle(ArchiveStockLevelCommand command, CancellationToken cancellationToken)
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
            return Result<bool>.Failure(Error.InsufficientSpacePrivileges("archive stock items"));
        }

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock == null)
        {
            return Result<bool>.Failure(Error.StockNotFound(command.SpaceId));
        }

        // Archiving is now automatic: a product drops off the active list once all its bags are
        // empty. This command is retained as a no-op success for backward compatibility with the
        // current UI until it is replaced by the per-bag "mark empty" action.
        await Task.CompletedTask;
        return Result<bool>.Success(true);
    }
}
