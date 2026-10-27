using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;

namespace BeanShare.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(UserId id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetBySystemRoleAsync(SystemRole role, CancellationToken ct = default);
    Task<(IReadOnlyList<User> Users, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? searchTerm = null,
        SystemRole? roleFilter = null,
        bool? activeFilter = null,
        CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
    Task<bool> ExistsAsync(UserId id, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<int> CountActiveAsync(CancellationToken ct = default);
    Task<int> CountByRoleAsync(SystemRole role, CancellationToken ct = default);

    /// <summary>
    /// Uncached, untracked primary-key projection for the access checks: every command and every
    /// merged-state check reads it, so a claim, a deactivation or a merge is seen at once in both heads.
    /// </summary>
    Task<UserAccessStatus> GetAccessStatusAsync(UserId id, CancellationToken ct = default);

    /// <summary>
    /// Of the given users, those merged into another (userId → target), uncached and not filtered by
    /// membership.
    /// </summary>
    Task<IReadOnlyDictionary<UserId, UserId>> GetMergedMapAsync(IReadOnlyCollection<UserId> userIds, CancellationToken ct = default);

    /// <summary>Uncached, untracked, case-insensitive lookup by email.</summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken ct = default);
}

/// <summary>What the access checks need to know about a user id.</summary>
public sealed record UserAccessStatus(bool Exists, bool IsActive, bool IsPlaceholder, UserId? MergedIntoUserId)
{
    public static readonly UserAccessStatus Missing = new(false, false, false, null);

    /// <summary>A real, active account that may act.</summary>
    public bool CanAct => Exists && IsActive && !IsPlaceholder;

    public bool IsMerged => MergedIntoUserId.HasValue;
}
