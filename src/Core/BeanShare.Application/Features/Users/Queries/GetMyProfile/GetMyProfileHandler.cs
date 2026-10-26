using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Services;
using MediatR;

namespace BeanShare.Application.Features.Users.Queries.GetMyProfile;

public sealed class GetMyProfileHandler : IRequestHandler<GetMyProfileQuery, Result<MyProfileDto>>
{
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;

    public GetMyProfileHandler(IUserService userService, IUserContext userContext)
    {
        _userService = userService;
        _userContext = userContext;
    }

    public async Task<Result<MyProfileDto>> Handle(GetMyProfileQuery query, CancellationToken cancellationToken)
    {
        var userId = _userContext.CurrentUserId;
        var user = await _userService.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result<MyProfileDto>.Failure(Error.UserNotFound(userId.Value));

        return Result<MyProfileDto>.Success(new MyProfileDto(
            user.Id.Value, user.Name, user.Email, user.PictureUrl, user.CreatedAt, user.RevolutTag));
    }
}
