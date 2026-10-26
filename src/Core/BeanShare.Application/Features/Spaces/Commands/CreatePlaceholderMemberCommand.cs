using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Spaces.Dtos;

namespace BeanShare.Application.Features.Spaces.Commands;

/// <summary>Adds a member without an account (D21): a name and an optional email used for a later link.</summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record CreatePlaceholderMemberCommand(Guid SpaceId, string Name, string? Email = null)
    : IAuthorize, ICommand<Result<MembershipDto>>;
