using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Balances.Commands.DeleteBalanceEntry;

public sealed class DeleteBalanceEntryHandler : IRequestHandler<DeleteBalanceEntryCommand, Result<bool>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IBalanceEntryRepository _balanceEntryRepository;

    public DeleteBalanceEntryHandler(ISpaceRepository spaceRepository, IBalanceEntryRepository balanceEntryRepository)
    {
        _spaceRepository = spaceRepository;
        _balanceEntryRepository = balanceEntryRepository;
    }

    public async Task<Result<bool>> Handle(DeleteBalanceEntryCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result<bool>.Failure(Error.SpaceNotFound(command.SpaceId));

        var entry = await _balanceEntryRepository.GetByIdAsync(new BalanceEntryId(command.EntryId), cancellationToken);
        if (entry is null || entry.SpaceId != spaceId)
            return Result<bool>.Failure(Error.NotFound("Balance entry", $"{command.EntryId} was not found"));

        if (entry.IsLocked)
            return Result<bool>.Failure(Error.EntryLocked());

        _balanceEntryRepository.Remove(entry);

        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);

        return Result<bool>.Success(true);
    }
}
