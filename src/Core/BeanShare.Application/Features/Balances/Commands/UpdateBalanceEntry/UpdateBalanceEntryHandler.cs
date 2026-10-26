using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Balances.Commands.UpdateBalanceEntry;

public sealed class UpdateBalanceEntryHandler : IRequestHandler<UpdateBalanceEntryCommand, Result<BalanceEntryDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IBalanceEntryRepository _balanceEntryRepository;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;

    public UpdateBalanceEntryHandler(
        ISpaceRepository spaceRepository,
        IBalanceEntryRepository balanceEntryRepository,
        IUserService userService,
        IUserContext userContext,
        IClock clock,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _spaceRepository = spaceRepository;
        _balanceEntryRepository = balanceEntryRepository;
        _userService = userService;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<BalanceEntryDto>> Handle(UpdateBalanceEntryCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result<BalanceEntryDto>.Failure(Error.SpaceNotFound(command.SpaceId));

        var entry = await _balanceEntryRepository.GetByIdAsync(new BalanceEntryId(command.EntryId), cancellationToken);
        if (entry is null || entry.SpaceId != spaceId)
            return Result<BalanceEntryDto>.Failure(Error.NotFound("Balance entry", $"{command.EntryId} was not found"));

        if (entry.IsLocked)
            return Result<BalanceEntryDto>.Failure(Error.EntryLocked());

        var userId = new UserId(command.UserId);
        var counterpartyId = new UserId(command.CounterpartyUserId);
        foreach (var party in new[] { userId, counterpartyId })
        {
            if (!space.HasMember(party))
                return Result<BalanceEntryDto>.Failure(Error.MemberNotFound(party.Value, command.SpaceId));
        }

        // An entry naming a merged user can still be deleted, but not edited (W5).
        if (await _userRepository.AnyMergedAsync(new[] { entry.UserId, entry.CounterpartyUserId, userId, counterpartyId }, cancellationToken))
            return Result<BalanceEntryDto>.Failure(Error.MemberMerged());

        var validationError = BalanceEntry.GetValidationError(
            userId, counterpartyId, command.Amount, space.Currency, command.EffectiveDate,
            command.Kind, command.Note, space.LedgerStartDate, _clock);
        if (validationError is not null)
            return Result<BalanceEntryDto>.Failure(Error.ValidationFailure("Entry", validationError));

        var currentUserId = _userContext.CurrentUserId;
        entry.Update(userId, counterpartyId, command.Amount, command.EffectiveDate, command.Kind, command.Note,
            currentUserId, space.LedgerStartDate, _clock);

        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);

        var dtos = await BalanceEntryMapping.ToDtosAsync(new[] { entry }, currentUserId, _userService, cancellationToken);
        return Result<BalanceEntryDto>.Success(dtos[0]);
    }
}
