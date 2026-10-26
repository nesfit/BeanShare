using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using MediatR;

namespace BeanShare.Application.Features.Admin.Users.Commands;
public sealed class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand, Result>
{
    private readonly IUserRepository _userRepository;
    private readonly IUserContext _userContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IUserService _userService;

    public DeactivateUserCommandHandler(
        IUserRepository userRepository,
        IUserContext userContext,
        IUnitOfWork unitOfWork,
        IClock clock,
        IUserService userService)
    {
        _userService = userService;
        _userRepository = userRepository;
        _userContext = userContext;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        var userId = new UserId(request.UserId);

        if (userId == _userContext.CurrentUserId)
        {
            return Result.Failure(Error.ValidationFailure("UserId", "Cannot deactivate your own account"));
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(Error.NotFound("User.NotFound", "User not found"));
        }

        if (!user.IsActive)
        {
            return Result.Failure(Error.ValidationFailure("IsActive", "User is already deactivated"));
        }

        user.Deactivate(_clock.UtcNow);

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _userService.Evict(user.Id, user.Email);

        return Result.Success();
    }
}
