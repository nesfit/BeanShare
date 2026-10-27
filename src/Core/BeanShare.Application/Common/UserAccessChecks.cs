using BeanShare.Application.Abstractions;
using BeanShare.Domain.Common;

namespace BeanShare.Application.Common;

/// <summary>Merged-state checks; every one reads uncached through <see cref="IUserRepository.GetAccessStatusAsync"/>.</summary>
public static class UserAccessChecks
{
    public static async Task<bool> IsMergedAsync(this IUserRepository users, UserId userId, CancellationToken cancellationToken = default) =>
        (await users.GetAccessStatusAsync(userId, cancellationToken))?.IsMerged == true;

    /// <summary>True when any of the users is a merged placeholder.</summary>
    public static async Task<bool> AnyMergedAsync(this IUserRepository users, IEnumerable<UserId> userIds, CancellationToken cancellationToken = default)
    {
        foreach (var userId in userIds.Distinct())
        {
            if (await users.IsMergedAsync(userId, cancellationToken))
                return true;
        }

        return false;
    }
}
