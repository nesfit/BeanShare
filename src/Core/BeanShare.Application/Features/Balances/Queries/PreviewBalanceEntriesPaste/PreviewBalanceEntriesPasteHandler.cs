using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Balances.Queries.PreviewBalanceEntriesPaste;

public sealed class PreviewBalanceEntriesPasteHandler
    : IRequestHandler<PreviewBalanceEntriesPasteQuery, Result<BalancePastePreviewDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly LedgerSnapshotService _ledgerSnapshotService;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;

    public PreviewBalanceEntriesPasteHandler(
        ISpaceRepository spaceRepository,
        LedgerSnapshotService ledgerSnapshotService,
        IUserService userService,
        IUserContext userContext,
        IClock clock,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _ledgerSnapshotService = ledgerSnapshotService;
        _spaceRepository = spaceRepository;
        _userService = userService;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<BalancePastePreviewDto>> Handle(PreviewBalanceEntriesPasteQuery query, CancellationToken cancellationToken)
    {
        if (PasteLimits.CheckLength(query.Text, nameof(query.Text)) is { } tooLong)
            return Result<BalancePastePreviewDto>.Failure(tooLong);

        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(new SpaceId(query.SpaceId)), cancellationToken);
        if (space is null)
            return Result<BalancePastePreviewDto>.Failure(Error.SpaceNotFound(query.SpaceId));

        var members = await MemberResolver.CreateAsync(space, _userService, _userRepository, cancellationToken);
        var batch = BalancePasteEvaluator.CreateBatch(
            space, _userContext.CurrentUserId, query.CounterpartyUserId, query.EffectiveDate, query.Kind, query.BatchNote, _clock);
        var evaluation = BalancePasteEvaluator.Evaluate(query.Text, batch, members, _clock, query.CreateMissingPlaceholders);

        // Each pasted row moves its amount off the counterparty, so its running balance changes by −Σ.
        var snapshot = await _ledgerSnapshotService.RunningAsync(space, cancellationToken);
        var counterpartyBalance = snapshot.Calculation.NetFor(batch.CounterpartyUserId);

        return Result<BalancePastePreviewDto>.Success(new BalancePastePreviewDto(
            evaluation.Rows,
            evaluation.Errors,
            evaluation.Rows.Count(r => r.IsValid),
            evaluation.Rows.Count(r => !r.IsValid),
            evaluation.Total,
            space.Currency.Code,
            batch.CounterpartyUserId.Value,
            members.NameOf(batch.CounterpartyUserId),
            batch.EffectiveDate,
            batch.Kind.ToString(),
            counterpartyBalance,
            counterpartyBalance - evaluation.Total,
            evaluation.PlaceholdersToCreate));
    }
}
