using BeanShare.Application.Constants;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BeanShare.Infrastructure.Services;

/// <summary>
/// Implementation of IUserService for retrieving user information
/// </summary>
public sealed class UserService : IUserService
{
    private readonly BeanShareDbContext _context;
    private readonly IMemoryCache _cache;
    private const string UserCacheKeyPrefix = "user_";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(CacheSettings.UserCacheDurationMinutes);

    public UserService(BeanShareDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{UserCacheKeyPrefix}{userId.Value}";

        if (_cache.TryGetValue<User>(cacheKey, out var cachedUser))
        {
            return cachedUser;
        }

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user != null)
        {
            _cache.Set(cacheKey, user, CacheDuration);
        }

        return user;
    }

    public async Task<User?> GetByIdForUpdateAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetByIdsAsync(IEnumerable<UserId> userIds, CancellationToken cancellationToken = default)
    {
        var idList = userIds.ToList();
        if (!idList.Any())
            return new List<User>();

        var result = new List<User>();
        var idsToFetch = new List<UserId>();

        foreach (var userId in idList)
        {
            var cacheKey = $"{UserCacheKeyPrefix}{userId.Value}";
            if (_cache.TryGetValue<User>(cacheKey, out var cachedUser))
            {
                result.Add(cachedUser!);
            }
            else
            {
                idsToFetch.Add(userId);
            }
        }

        if (idsToFetch.Any())
        {
            // TODO: This is N+1 query but needed because EF Core can't translate Contains() with value objects
            // Maybe use raw SQL or store GUIDs separately if this becomes a bottleneck
            foreach (var userId in idsToFetch)
            {
                var user = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

                if (user != null)
                {
                    var cacheKey = $"{UserCacheKeyPrefix}{user.Id.Value}";
                    _cache.Set(cacheKey, user, CacheDuration);
                    result.Add(user);
                }
            }
        }

        return result;
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var emailLower = email.ToLowerInvariant();
        var cacheKey = $"{UserCacheKeyPrefix}email_{emailLower}";

        if (_cache.TryGetValue<User>(cacheKey, out var cachedUser))
        {
            return cachedUser;
        }

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == emailLower, cancellationToken);

        if (user != null)
        {
            _cache.Set(cacheKey, user, CacheDuration);
            var idCacheKey = $"{UserCacheKeyPrefix}{user.Id.Value}";
            _cache.Set(idCacheKey, user, CacheDuration);
        }

        return user;
    }

    public async Task<User?> GetByProviderUserIdAsync(
        AuthenticationProvider provider,
        string providerUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerUserId))
        {
            return null;
        }

        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Provider == provider && u.ProviderUserId == providerUserId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var users = await _context.Users
            .AsNoTracking()
            .OrderBy(u => u.Name)
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            var cacheKey = $"{UserCacheKeyPrefix}{user.Id.Value}";
            _cache.Set(cacheKey, user, CacheDuration);
        }

        return users;
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);

        var cacheKey = $"{UserCacheKeyPrefix}{user.Id.Value}";
        _cache.Remove(cacheKey);

        var emailCacheKey = $"{UserCacheKeyPrefix}email_{user.Email.ToLowerInvariant()}";
        _cache.Remove(emailCacheKey);
    }

    public void Evict(UserId userId, params string[] emails)
    {
        _cache.Remove($"{UserCacheKeyPrefix}{userId.Value}");
        foreach (var email in emails.Where(e => !string.IsNullOrWhiteSpace(e)))
            _cache.Remove($"{UserCacheKeyPrefix}email_{email.Trim().ToLowerInvariant()}");
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await _context.Users.AddAsync(user, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
