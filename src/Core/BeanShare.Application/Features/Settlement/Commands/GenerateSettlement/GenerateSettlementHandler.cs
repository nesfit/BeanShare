using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;

/// <summary>
/// Generates the ledger settlement of a closed period: guards G1–G5, one snapshot line per non-zero
/// position plus the treasurer, the previous settlement's open lines carried in, and every included
/// cup and entry stamped in the same unit of work.
/// </summary>
public sealed class GenerateSettlementHandler : IRequestHandler<GenerateSettlementCommand, Result<SettlementDto>>
{
    private readonly ISettlementRepository _settlementRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly LedgerSnapshotService _ledgerSnapshotService;
    private readonly IUserContext _userContext;
    private readonly IUserService _userService;
    private readonly IClock _clock;

    public GenerateSettlementHandler(
        ISettlementRepository settlementRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ISpaceRepository spaceRepository,
        LedgerSnapshotService ledgerSnapshotService,
        IUserContext userContext,
        IUserService userService,
        IClock clock)
    {
        _settlementRepository = settlementRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _spaceRepository = spaceRepository;
        _ledgerSnapshotService = ledgerSnapshotService;
        _userContext = userContext;
        _userService = userService;
        _clock = clock;
    }

    public async Task<Result<SettlementDto>> Handle(GenerateSettlementCommand command, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;
        var billingPeriodId = new BillingPeriodId(command.BillingPeriodId);

        var billingPeriod = await _billingPeriodRepository.GetByIdAsync(billingPeriodId, cancellationToken);
        if (billingPeriod == null)
        {
            return Result<SettlementDto>.Failure(Error.BillingPeriodNotFound(command.BillingPeriodId));
        }

        // G1 (repeated in the guards, which the preview shares)
        if (billingPeriod.State != Domain.Enums.BillingState.Closed)
        {
            return Result<SettlementDto>.Failure(
                Error.InvalidBillingPeriodState("generate settlement", billingPeriod.State.ToString()));
        }

        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(billingPeriod.SpaceId), cancellationToken);
        if (space == null)
        {
            return Result<SettlementDto>.Failure(Error.SpaceNotFound(billingPeriod.SpaceId.Value));
        }

        if (!space.IsAdmin(currentUserId))
        {
            return Result<SettlementDto>.Failure(Error.InsufficientSpacePrivileges("generate settlements"));
        }

        var spacePeriods = await _billingPeriodRepository.GetBySpaceIdAsync(space.Id, cancellationToken);
        if (await SettlementGuards.CheckAsync(space, billingPeriod, spacePeriods, _userService, cancellationToken) is { } guardError)
        {
            return Result<SettlementDto>.Failure(guardError);
        }

        // G5
        if (await _settlementRepository.GetByBillingPeriodIdAsync(billingPeriodId, cancellationToken) != null)
        {
            return Result<SettlementDto>.Failure(Error.SettlementAlreadyExists(command.BillingPeriodId));
        }

        var snapshot = await _ledgerSnapshotService.ForPeriodAsync(space, billingPeriod, forUpdate: true, cancellationToken);
        var sequence = await _settlementRepository.GetMaxSequenceAsync(space.Id, cancellationToken) + 1;

        var settlement = Domain.Aggregates.Settlement.Settlement.CreateLedger(
            space.Id,
            billingPeriodId,
            space.Currency,
            currentUserId,
            space.TreasurerUserId!.Value,
            sequence,
            _clock);

        var previous = snapshot.PreviousSettlement;
        previous?.CarryForwardTo(settlement.Id, _clock);

        foreach (var line in snapshot.Calculation.Lines)
        {
            settlement.AddLedgerLine(line, previous?.Id, _clock);
        }

        foreach (var cup in snapshot.Cups.Where(c => c.BillingPeriodId is null))
        {
            cup.AssignToBillingPeriod(billingPeriodId);
        }

        foreach (var entry in snapshot.Entries)
        {
            entry.MarkIncluded(settlement.Id);
        }

        settlement.FinalizeGeneration(_clock);
        await _settlementRepository.AddAsync(settlement, cancellationToken);

        billingPeriod.MarkAsSettled(currentUserId, _clock);
        await _billingPeriodRepository.UpdateAsync(billingPeriod, cancellationToken);

        // Serializes generation against settings changes, member removal and other generations.
        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);

        var users = await _userService.GetByIdsAsync(SettlementDtoMapper.UserIdsOf(settlement), cancellationToken);
        var dto = SettlementDtoMapper.ToDto(settlement, billingPeriod, users.ToDictionary(u => u.Id));

        return Result<SettlementDto>.Success(dto);
    }
}
