namespace BeanShare.Contracts.Spaces;

public sealed record GetUserSpacesResponse
{
    public required IReadOnlyList<SpaceListItem> Spaces { get; init; }
}

public sealed record SpaceListItem
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string InviteCode { get; init; }
    public required int MemberCount { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string UserRole { get; init; }
}

public sealed record GetSpaceByIdRequest
{
    public required Guid SpaceId { get; init; }
}

public sealed record GetSpaceByIdResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string InviteCode { get; init; }
    public required int MemberCount { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required IReadOnlyList<SpaceMember> Members { get; init; }
}

public sealed record SpaceMember
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required DateTime JoinedAt { get; init; }
    public string UserName { get; init; } = "Unknown User";

    // A member without an account (no login).
    public bool IsPlaceholder { get; init; }
}