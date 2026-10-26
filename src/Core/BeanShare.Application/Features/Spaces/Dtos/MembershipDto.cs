namespace BeanShare.Application.Features.Spaces.Dtos
{
    public sealed record MembershipDto
    {
        public required Guid UserId { get; init; }
        public required string Email { get; init; }
        public required string Role { get; init; }
        public required DateTime JoinedAt { get; init; }

        public string UserName { get; init; } = "Unknown User";

        /// <summary>A member without an account (D21); shown with a "no login" badge.</summary>
        public bool IsPlaceholder { get; init; }
    }
}
