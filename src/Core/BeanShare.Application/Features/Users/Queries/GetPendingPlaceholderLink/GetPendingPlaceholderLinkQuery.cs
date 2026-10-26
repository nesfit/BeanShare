using MediatR;

namespace BeanShare.Application.Features.Users.Queries.GetPendingPlaceholderLink;

/// <summary>
/// The placeholder a signed-in person was matched to, for the link page (DS-31). A plain query on purpose,
/// neither a command nor <c>IAuthorize</c>: the person has no user row yet, so the access check would refuse
/// it. Its handler is the guard: it reads only the verified email of a pending principal and takes no id.
/// </summary>
public sealed record GetPendingPlaceholderLinkQuery : IRequest<PendingPlaceholderLinkDto?>;

/// <summary>The spaces the placeholder belongs to, with its running balance in each.</summary>
public sealed record PendingPlaceholderLinkDto(string PlaceholderName, string Email, IReadOnlyList<PendingPlaceholderSpaceDto> Spaces);

/// <param name="Balance">The running balance; positive means the space owes the person.</param>
public sealed record PendingPlaceholderSpaceDto(Guid SpaceId, string SpaceName, string Currency, decimal Balance);
