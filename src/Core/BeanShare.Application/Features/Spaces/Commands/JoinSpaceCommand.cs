using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.Spaces.Commands;

/// <summary>Joins by invite code. <see cref="IAuthorize"/> without an attribute: the caller must be signed in (DS-30).</summary>
public sealed record JoinSpaceCommand(string InviteCode) : IAuthorize, ICommand<Result<JoinSpaceResult>>;

public sealed record JoinSpaceResult(Guid SpaceId, string SpaceName);
