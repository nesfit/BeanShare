using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.Users.Queries.GetMyProfile;

public sealed record GetMyProfileQuery : IAuthorize, IQuery<Result<MyProfileDto>>;

public sealed record MyProfileDto(
    Guid Id,
    string Name,
    string Email,
    string? PictureUrl,
    DateTime CreatedAt,
    string? RevolutTag);
