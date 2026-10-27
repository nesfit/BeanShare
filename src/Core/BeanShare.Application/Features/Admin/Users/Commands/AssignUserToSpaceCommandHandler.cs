using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Admin.Users.Commands;
public sealed class AssignUserToSpaceCommandHandler : IRequestHandler<AssignUserToSpaceCommand, Result>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public AssignUserToSpaceCommandHandler(
        ISpaceRepository spaceRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _spaceRepository = spaceRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(AssignUserToSpaceCommand request, CancellationToken cancellationToken)
    {
        var userId = new UserId(request.UserId);
        var spaceId = new SpaceId(request.SpaceId);

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.UserNotFound(request.UserId));
        }

        // A placeholder belongs to the one space it was created in: a merge there would also move its money in any other
        // space, to a target who is no member of it. A merged user is no person any more.
        if (user.IsMerged)
        {
            return Result.Failure(Error.MemberMerged());
        }

        if (user.IsPlaceholder)
        {
            return Result.Failure(Error.ValidationFailure("UserId", "A placeholder member cannot be assigned to another space"));
        }

        var space = await _spaceRepository.GetByIdAsync(spaceId, cancellationToken);
        if (space is null)
        {
            return Result.Failure(Error.SpaceNotFound(request.SpaceId));
        }

        if (space.HasMember(userId))
        {
            return Result.Failure(Error.AlreadySpaceMember(request.SpaceId, request.UserId));
        }

        space.Join(userId, _clock);

        await _spaceRepository.UpdateAsync(space, cancellationToken);

        return Result.Success();
    }
}