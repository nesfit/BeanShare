using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Tests.Integration.Fixtures;

/// <summary>
/// The user rows the API's access check needs (DS-30): every command and IAuthorize request requires an
/// existing, active, non-placeholder user. Built as password users without a provider link, so the mock
/// principal never matches one by subject (U2).
/// </summary>
public static class FixtureUsers
{
    public static readonly Guid DefaultUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid SecondUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static async Task SeedAsync(IServiceProvider services)
    {
        await EnsureUserAsync(services, DefaultUserId, "test@coffeespace.local");
        await EnsureUserAsync(services, SecondUserId, "second@test.com");
    }

    public static async Task EnsureUserAsync(IServiceProvider services, Guid id, string email)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BeanShareDbContext>();
        var userId = new UserId(id);
        if (await db.Users.AnyAsync(u => u.Id == userId))
            return;

        db.Users.Add(User.CreateWithIdAndPassword(id, email, email.Split('@')[0], "not-a-real-hash", DateTime.UtcNow));
        await db.SaveChangesAsync();
    }
}
