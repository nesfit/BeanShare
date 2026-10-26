using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Spaces.Commands;
public sealed class LeaveSpaceCommandHandler : IRequestHandler<LeaveSpaceCommand, Result<LeaveSpaceResult>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly LedgerSnapshotService _ledgerSnapshotService;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;

    public LeaveSpaceCommandHandler(
        ISpaceRepository spaceRepository,
        ISettlementRepository settlementRepository,
        LedgerSnapshotService ledgerSnapshotService,
        IUserContext userContext,
        IClock clock,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _spaceRepository = spaceRepository;
        _settlementRepository = settlementRepository;
        _ledgerSnapshotService = ledgerSnapshotService;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<LeaveSpaceResult>> Handle(LeaveSpaceCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var specification = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(specification, cancellationToken);

        if (space == null)
        {
            return Result<LeaveSpaceResult>.Failure(Error.SpaceNotFound(command.SpaceId));
        }

        var currentUserId = _userContext.CurrentUserId;
        var member = space.GetMember(currentUserId);

        if (member == null)
        {
            return Result<LeaveSpaceResult>.Failure(Error.MemberNotFound(currentUserId.Value, command.SpaceId));
        }

        if (member.Role == SpaceRole.Admin)
        {
            return Result<LeaveSpaceResult>.Failure(Error.InsufficientSpacePrivileges(
                "leave this space as an admin. Please transfer admin rights first or ask another admin to remove you"));
        }

        if (await MemberDepartureGuard.CheckAsync(space, currentUserId, _settlementRepository, _ledgerSnapshotService, _userRepository, cancellationToken) is { } departureError)
        {
            return Result<LeaveSpaceResult>.Failure(departureError);
        }

        try
        {
            var spaceName = space.Name;
            space.RemoveMember(currentUserId, _clock);
            await _spaceRepository.UpdateAsync(space, cancellationToken);

            return Result<LeaveSpaceResult>.Success(new LeaveSpaceResult(command.SpaceId, spaceName));
        }
        catch (Exception ex) when (ex is SpaceDomainException || ex is InvariantViolationException)
        {
            return Result<LeaveSpaceResult>.Failure(Error.SystemFailure("leave space"));
        }
    }
}
