using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Domain.Common;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using Mapster;
using MediatR;

namespace BeanShare.Application.Features.Spaces.Commands;
public sealed class PromoteMemberCommandHandler : IRequestHandler<PromoteMemberCommand, Result<MembershipDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;

    public PromoteMemberCommandHandler(
        ISpaceRepository spaceRepository,
        IUserContext userContext,
        IClock clock,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<MembershipDto>> Handle(PromoteMemberCommand request, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(request.SpaceId);
        var specification = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(specification, cancellationToken);

        if (space == null)
        {
            return Result<MembershipDto>.Failure(Error.SpaceNotFound(request.SpaceId));
        }

        var currentUserId = _userContext.CurrentUserId;
        if (!space.IsAdmin(currentUserId))
        {
            return Result<MembershipDto>.Failure(Error.InsufficientSpacePrivileges("promote members"));
        }

        var targetUserId = new UserId(request.UserId);
        var targetMember = space.GetMember(targetUserId);

        if (targetMember == null)
        {
            return Result<MembershipDto>.Failure(Error.MemberNotFound(request.UserId, request.SpaceId));
        }

        if ((await _userRepository.GetAccessStatusAsync(targetUserId, cancellationToken))?.IsPlaceholder == true)
        {
            return Result<MembershipDto>.Failure(Error.CannotPromoteMember("a placeholder has no account and cannot be an admin"));
        }

        try
        {
            space.PromoteMember(targetUserId, _clock);
            await _spaceRepository.UpdateAsync(space, cancellationToken);

            var updatedMember = space.GetMember(targetUserId)!;
            var membershipDto = updatedMember.Adapt<MembershipDto>();

            return Result<MembershipDto>.Success(membershipDto);
        }
        catch (SpaceDomainException ex)
        {
            return Result<MembershipDto>.Failure(Error.CannotPromoteMember(ex.Message));
        }
    }
}