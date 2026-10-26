using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MapsterMapper;
using MediatR;

namespace BeanShare.Application.Features.Spaces.Commands;
public sealed class RemoveMemberCommandHandler : IRequestHandler<RemoveMemberCommand, Result<MemberActionDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly LedgerSnapshotService _ledgerSnapshotService;
    private readonly IUserContext _userContext;
    private readonly IMapper _mapper;
    private readonly IClock _clock;

    public RemoveMemberCommandHandler(
        ISpaceRepository spaceRepository,
        ISettlementRepository settlementRepository,
        LedgerSnapshotService ledgerSnapshotService,
        IUserContext userContext,
        IMapper mapper,
        IClock clock)
    {
        _spaceRepository = spaceRepository;
        _settlementRepository = settlementRepository;
        _ledgerSnapshotService = ledgerSnapshotService;
        _userContext = userContext;
        _mapper = mapper;
        _clock = clock;
    }

    public async Task<Result<MemberActionDto>> Handle(RemoveMemberCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var specification = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(specification, cancellationToken);

        if (space == null)
        {
            return Result<MemberActionDto>.Failure(Error.SpaceNotFound(command.SpaceId));
        }

        var currentUserId = _userContext.CurrentUserId;
        var targetUserId = new UserId(command.UserId);
        var targetMember = space.GetMember(targetUserId);

        if (targetMember == null)
        {
            return Result<MemberActionDto>.Failure(Error.MemberNotFound(command.UserId, command.SpaceId));
        }

        var isSelfRemoval = currentUserId == targetUserId;
        var isAdminRemoval = space.IsAdmin(currentUserId);

        if (!isSelfRemoval && !isAdminRemoval)
        {
            return Result<MemberActionDto>.Failure(Error.InsufficientSpacePrivileges("remove members"));
        }

        if (targetMember.Role == SpaceRole.Admin && space.AdminCount <= 1)
        {
            return Result<MemberActionDto>.Failure(Error.CannotRemoveLastAdmin());
        }

        if (await MemberDepartureGuard.CheckAsync(space, targetUserId, _settlementRepository, _ledgerSnapshotService, cancellationToken) is { } departureError)
        {
            return Result<MemberActionDto>.Failure(departureError);
        }

        try
        {
            space.RemoveMember(targetUserId, _clock);
            await _spaceRepository.UpdateAsync(space, cancellationToken);

            var dto = new MemberActionDto
            {
                SpaceId = command.SpaceId,
                UserId = command.UserId,
                Action = "removed",
                Timestamp = _clock.UtcNow
            };

            return Result<MemberActionDto>.Success(dto);
        }
        catch (Exception ex) when (ex is SpaceDomainException || ex is InvariantViolationException)
        {
            return Result<MemberActionDto>.Failure(Error.SystemFailure("remove member"));
        }
    }
}