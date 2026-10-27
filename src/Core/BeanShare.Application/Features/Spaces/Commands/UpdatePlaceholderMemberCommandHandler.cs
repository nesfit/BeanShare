using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Application.Features.Spaces.Services;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Spaces.Commands;

public sealed class UpdatePlaceholderMemberCommandHandler : IRequestHandler<UpdatePlaceholderMemberCommand, Result<MembershipDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserService _userService;

    public UpdatePlaceholderMemberCommandHandler(ISpaceRepository spaceRepository, IUserRepository userRepository, IUserService userService)
    {
        _spaceRepository = spaceRepository;
        _userRepository = userRepository;
        _userService = userService;
    }

    public async Task<Result<MembershipDto>> Handle(UpdatePlaceholderMemberCommand command, CancellationToken cancellationToken)
    {
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(new SpaceId(command.SpaceId)), cancellationToken);
        if (space is null)
            return Result<MembershipDto>.Failure(Error.SpaceNotFound(command.SpaceId));

        var userId = new UserId(command.UserId);
        var membership = space.GetMember(userId);
        if (membership is null)
            return Result<MembershipDto>.Failure(Error.MemberNotFound(command.UserId, command.SpaceId));

        // Read uncached first: a merge in the other head must be seen at once (X3).
        var status = await _userRepository.GetAccessStatusAsync(userId, cancellationToken);
        if (status?.IsMerged == true)
            return Result<MembershipDto>.Failure(Error.MemberMerged());

        var placeholder = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (placeholder is null)
            return Result<MembershipDto>.Failure(Error.UserNotFound(command.UserId));
        if (placeholder.IsMerged)
            return Result<MembershipDto>.Failure(Error.MemberMerged()); // merged since the uncached read above
        if (!placeholder.IsPlaceholder)
            return Result<MembershipDto>.Failure(Error.NotPlaceholder());

        if (PlaceholderMembers.ValidateName(command.Name) is { } nameError)
            return Result<MembershipDto>.Failure(nameError);

        if (!string.IsNullOrWhiteSpace(command.Email))
        {
            if (PlaceholderMembers.ValidateEmail(command.Email) is { } emailError)
                return Result<MembershipDto>.Failure(emailError);
            if (await _userRepository.FindByEmailAsync(command.Email, cancellationToken) is { } owner && owner.Id != userId)
                return Result<MembershipDto>.Failure(Error.EmailInUse());
        }

        var oldEmail = placeholder.Email;
        placeholder.UpdatePlaceholder(command.Name, command.Email);

        // A claim of this placeholder bumps its spaces' ledger token too, so an edit racing it fails instead of both
        // committing (the claimed account must not take the address the admin just typed).
        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);
        _userService.Evict(placeholder.Id, oldEmail, placeholder.Email);

        return Result<MembershipDto>.Success(new MembershipDto
        {
            UserId = placeholder.Id.Value,
            Email = placeholder.PublicEmail,
            Role = membership.Role.ToString(),
            JoinedAt = membership.JoinedAt,
            UserName = placeholder.Name,
            IsPlaceholder = true
        });
    }
}
