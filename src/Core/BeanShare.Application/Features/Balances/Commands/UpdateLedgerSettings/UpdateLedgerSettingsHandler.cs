using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Features.CoffeeStock;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;

public sealed class UpdateLedgerSettingsHandler : IRequestHandler<UpdateLedgerSettingsCommand, Result<LedgerSettingsDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly IUserService _userService;
    private readonly IUserRepository _userRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;

    public UpdateLedgerSettingsHandler(
        ISpaceRepository spaceRepository,
        ISettlementRepository settlementRepository,
        IUserService userService,
        IUserRepository userRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ICoffeeStockRepository coffeeStockRepository)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _userRepository = userRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _spaceRepository = spaceRepository;
        _settlementRepository = settlementRepository;
        _userService = userService;
    }

    public async Task<Result<LedgerSettingsDto>> Handle(UpdateLedgerSettingsCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result<LedgerSettingsDto>.Failure(Error.SpaceNotFound(command.SpaceId));

        // A null value keeps the current setting; only the Clear flags remove one.
        var treasurer = command.ClearTreasurer
            ? null
            : command.TreasurerUserId is { } id ? new UserId(id) : space.TreasurerUserId;
        if (treasurer is { } treasurerId && treasurer != space.TreasurerUserId)
        {
            if (!space.HasMember(treasurerId))
                return Result<LedgerSettingsDto>.Failure(Error.MemberNotFound(treasurerId.Value, command.SpaceId));
            if (!space.IsAdmin(treasurerId))
                return Result<LedgerSettingsDto>.Failure(Error.ValidationFailure(
                    nameof(command.TreasurerUserId), "The treasurer must be an admin of the space"));
            if ((await _userRepository.GetAccessStatusAsync(treasurerId, cancellationToken))?.CanAct != true)
                return Result<LedgerSettingsDto>.Failure(Error.ValidationFailure(
                    nameof(command.TreasurerUserId), "The treasurer must be an active member with an account, not a placeholder"));
        }

        var hasLedgerSettlement = await _settlementRepository.HasLedgerSettlementAsync(spaceId, cancellationToken);
        var ledgerStart = command.ClearLedgerStartDate
            ? null
            : command.LedgerStartDate is { } start ? UtcDate.Of(start) : space.LedgerStartDate;
        if (hasLedgerSettlement && ledgerStart != space.LedgerStartDate)
            return Result<LedgerSettingsDto>.Failure(Error.LedgerStartLocked());

        // A "Paid in Excel" bag must stay before the ledger start, so C cannot move onto or before it, and
        // cannot be cleared while any bag is flagged (H4, I4).
        if (ledgerStart != space.LedgerStartDate
            && await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken) is { } stock
            && stock.Purchases.Where(p => p.ExcludedFromLedgerCredit).ToList() is { Count: > 0 } flagged
            && flagged.FirstOrDefault(p => ledgerStart is not { } start || p.PurchasedAt >= start) is { } blocking)
        {
            return Result<LedgerSettingsDto>.Failure(Error.BagPaidInExcel(BagNames.Of(blocking)));
        }

        // History never touches money: every imported period must end before the ledger starts (DS-37), so while
        // history exists the start can neither move into it nor be cleared.
        if (ledgerStart != space.LedgerStartDate)
        {
            var periods = await _billingPeriodRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
            if (periods.Where(p => p.IsHistorical).Select(p => (DateTime?)p.EndDate).Max() is { } historyEnd
                && (ledgerStart is not { } newStart || historyEnd >= newStart))
            {
                return Result<LedgerSettingsDto>.Failure(Error.ValidationFailure(
                    nameof(command.LedgerStartDate),
                    $"The ledger start date must stay after the imported history, which ends {historyEnd:yyyy-MM-dd}"));
            }
        }

        if (treasurer != space.TreasurerUserId)
            space.SetTreasurer(treasurer);
        space.SetLedgerStartDate(ledgerStart, hasLedgerSettlement);

        // Serializes settings changes against generation, demotion and removal (DS-28).
        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);

        return Result<LedgerSettingsDto>.Success(
            await LedgerSettingsMapping.ToDtoAsync(space, hasLedgerSettlement, _userService, cancellationToken));
    }
}
