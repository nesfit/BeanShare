using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.Spaces.Commands;

/// <summary>
/// Merges a placeholder into an existing member (DS-32). No entry is written: every later calculation
/// sweeps the placeholder's Net to the target, so anything that still reaches it ends there too.
/// </summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record MergePlaceholderCommand(Guid SpaceId, Guid PlaceholderUserId, Guid TargetUserId)
    : IAuthorize, ICommand<Result>;
