using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.History.Commands.DeleteHistoryImport;

public sealed class DeleteHistoryImportHandler : IRequestHandler<DeleteHistoryImportCommand, Result>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IHistoryImportRepository _historyImportRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;

    public DeleteHistoryImportHandler(
        ISpaceRepository spaceRepository,
        IHistoryImportRepository historyImportRepository,
        IBillingPeriodRepository billingPeriodRepository)
    {
        _spaceRepository = spaceRepository;
        _historyImportRepository = historyImportRepository;
        _billingPeriodRepository = billingPeriodRepository;
    }

    public async Task<Result> Handle(DeleteHistoryImportCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result.Failure(Error.SpaceNotFound(command.SpaceId));

        var importId = new HistoryImportId(command.ImportId);
        var import = await _historyImportRepository.GetByIdAsync(importId, cancellationToken);
        if (import is null || import.SpaceId != spaceId)
            return Result.Failure(Error.NotFound("History import", $"{command.ImportId} was not found"));

        // Only this import's periods go; other imports and every live period stay.
        foreach (var period in (await _billingPeriodRepository.GetBySpaceIdAsync(spaceId, cancellationToken))
                     .Where(p => p.IsHistorical && p.HistoryImportId == importId))
        {
            _billingPeriodRepository.Remove(period);
        }

        _historyImportRepository.Remove(import);
        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);

        return Result.Success();
    }
}
