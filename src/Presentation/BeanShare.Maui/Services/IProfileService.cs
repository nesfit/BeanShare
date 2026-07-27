namespace BeanShare.Maui.Services;

public interface IProfileService
{
    /// <summary>Gets the current user's Revolut handle (bare, no @ or URL), or null if unset.</summary>
    Task<string?> GetRevolutTagAsync();

    /// <summary>Sets (or clears, with null/empty) the current user's Revolut handle.</summary>
    Task<bool> SetRevolutTagAsync(string? revolutTag);
}
