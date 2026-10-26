using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Features.Balances.Services;
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

    public UpdateLedgerSettingsHandler(
        ISpaceRepository spaceRepository,
        ISettlementRepository settlementRepository,
        IUserService userService)
    {
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
        }

        var hasLedgerSettlement = await _settlementRepository.HasLedgerSettlementAsync(spaceId, cancellationToken);
        var ledgerStart = command.ClearLedgerStartDate
            ? null
            : command.LedgerStartDate is { } start ? UtcDate.Of(start) : space.LedgerStartDate;
        if (hasLedgerSettlement && ledgerStart != space.LedgerStartDate)
            return Result<LedgerSettingsDto>.Failure(Error.LedgerStartLocked());

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
