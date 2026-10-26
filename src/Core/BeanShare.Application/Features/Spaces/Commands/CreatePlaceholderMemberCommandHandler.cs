using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Application.Features.Spaces.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Spaces.Commands;

public sealed class CreatePlaceholderMemberCommandHandler : IRequestHandler<CreatePlaceholderMemberCommand, Result<MembershipDto>>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClock _clock;

    public CreatePlaceholderMemberCommandHandler(ISpaceRepository spaceRepository, IUserRepository userRepository, IClock clock)
    {
        _spaceRepository = spaceRepository;
        _userRepository = userRepository;
        _clock = clock;
    }

    public async Task<Result<MembershipDto>> Handle(CreatePlaceholderMemberCommand command, CancellationToken cancellationToken)
    {
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(new SpaceId(command.SpaceId)), cancellationToken);
        if (space is null)
            return Result<MembershipDto>.Failure(Error.SpaceNotFound(command.SpaceId));

        var added = await PlaceholderMembers.AddAsync(space, command.Name, command.Email, _userRepository, _clock, cancellationToken);
        if (added.IsFailure)
            return Result<MembershipDto>.Failure(added.Errors.ToArray());

        await _spaceRepository.UpdateAsync(space, cancellationToken);

        var placeholder = added.Value;
        var membership = space.GetMember(placeholder.Id)!;
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
