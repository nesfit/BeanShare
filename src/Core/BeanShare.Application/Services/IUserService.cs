using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;

namespace BeanShare.Application.Services;

/// <summary>
/// Service for retrieving user information
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Get a user by their ID
    /// </summary>
    Task<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a user by their ID for updating (tracked entity)
    /// </summary>
    Task<User?> GetByIdForUpdateAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get multiple users by their IDs
    /// </summary>
    Task<IReadOnlyList<User>> GetByIdsAsync(IEnumerable<UserId> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a user by their email address
    /// </summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a user by external authentication provider and provider-specific user ID.
    /// </summary>
    Task<User?> GetByProviderUserIdAsync(AuthenticationProvider provider, string providerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all users (for admin purposes)
    /// </summary>
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update user preferences
    /// </summary>
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new user to the database
    /// </summary>
    Task AddAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the cached copies of a user (by id and by each given email), after a claim, decline, merge or
    /// placeholder edit changed the row behind the cache.
    /// </summary>
    void Evict(UserId userId, params string[] emails);
}
