using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Services;
using MediatR;

namespace BeanShare.Application.Features.Users.Commands.SetRevolutTag;

public sealed class SetRevolutTagHandler : IRequestHandler<SetRevolutTagCommand, Result>
{
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;

    public SetRevolutTagHandler(IUserService userService, IUserContext userContext)
    {
        _userService = userService;
        _userContext = userContext;
    }

    public async Task<Result> Handle(SetRevolutTagCommand command, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;

        var user = await _userService.GetByIdForUpdateAsync(currentUserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.UserNotFound(currentUserId.Value));
        }

        user.SetRevolutTag(command.RevolutTag);
        await _userService.UpdateAsync(user, cancellationToken);

        return Result.Success();
    }
}
