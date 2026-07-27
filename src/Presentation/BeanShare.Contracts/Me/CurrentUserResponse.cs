namespace BeanShare.Contracts.Me;

public sealed class CurrentUserResponse
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public List<string> Roles { get; init; } = [];

    /// <summary>The user's Revolut handle (bare, no @ or URL), used for settlement 'Pay me' links.</summary>
    public string? RevolutTag { get; init; }
}