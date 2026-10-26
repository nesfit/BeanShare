using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;
using BeanShare.Application.Features.Spaces.Dtos;

namespace BeanShare.Application.Features.Spaces.Commands;

/// <summary>Renames a placeholder or changes its email (none = the sentinel). A merged one is refused (X3).</summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record UpdatePlaceholderMemberCommand(Guid SpaceId, Guid UserId, string Name, string? Email = null)
    : IAuthorize, ICommand<Result<MembershipDto>>;
