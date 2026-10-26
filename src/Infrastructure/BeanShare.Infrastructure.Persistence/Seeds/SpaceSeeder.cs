using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace BeanShare.Infrastructure.Persistence.Seeds;

public class SpaceSeeder : IDataSeeder
{
    public int Order => 2;

    public async Task SeedAsync(BeanShareDbContext context, CancellationToken cancellationToken = default)
    {
        if (await context.Spaces.AnyAsync(cancellationToken))
            return;

        var clock = new FixedClock(DateTime.UtcNow.AddMonths(-6));
        var spaces = GetSeedSpaces(clock);
        await context.Spaces.AddRangeAsync(spaces, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static List<Space> GetSeedSpaces(IClock clock)
    {
        var spaces = new List<Space>();

        var engineeringSpace = Space.Create(
            new SpaceId(new Guid("aaaa1111-aaaa-1111-aaaa-111111111111")),
            "Engineering Team",
            Currency.USD,
            new UserId(new Guid("11111111-1111-1111-1111-111111111111")),
            new InviteCode("ENG2K24"),
            clock
        );

        engineeringSpace.Join(new UserId(new Guid("22222222-2222-2222-2222-222222222222")), clock);
        engineeringSpace.Join(new UserId(new Guid("33333333-3333-3333-3333-333333333333")), clock);
        engineeringSpace.Join(new UserId(new Guid("44444444-4444-4444-4444-444444444444")), clock);
        engineeringSpace.PromoteMember(new UserId(new Guid("44444444-4444-4444-4444-444444444444")), clock);
        engineeringSpace.Join(new UserId(new Guid("55555555-5555-5555-5555-555555555555")), clock);
        engineeringSpace.Join(new UserId(new Guid("88888888-8888-8888-8888-888888888888")), clock);

        spaces.Add(engineeringSpace);

        var marketingSpace = Space.Create(
            new SpaceId(new Guid("bbbb2222-bbbb-2222-bbbb-222222222222")),
            "Marketing Office",
            Currency.EUR,
            new UserId(new Guid("22222222-2222-2222-2222-222222222222")),
            new InviteCode("MKT2K24"),
            clock
        );

        marketingSpace.Join(new UserId(new Guid("44444444-4444-4444-4444-444444444444")), clock);
        marketingSpace.Join(new UserId(new Guid("66666666-6666-6666-6666-666666666666")), clock);
        marketingSpace.PromoteMember(new UserId(new Guid("66666666-6666-6666-6666-666666666666")), clock);
        marketingSpace.Join(new UserId(new Guid("77777777-7777-7777-7777-777777777777")), clock);

        spaces.Add(marketingSpace);

        var remoteSpace = Space.Create(
            new SpaceId(new Guid("cccc3333-cccc-3333-cccc-333333333333")),
            "Remote Workers Hub",
            Currency.CZK,
            new UserId(new Guid("33333333-3333-3333-3333-333333333333")),
            new InviteCode("RMTE24"),
            clock
        );

        remoteSpace.Join(new UserId(new Guid("11111111-1111-1111-1111-111111111111")), clock);
        remoteSpace.Join(new UserId(new Guid("55555555-5555-5555-5555-555555555555")), clock);
        remoteSpace.Join(new UserId(new Guid("66666666-6666-6666-6666-666666666666")), clock);
        remoteSpace.Join(new UserId(new Guid("88888888-8888-8888-8888-888888888888")), clock);
        remoteSpace.PromoteMember(new UserId(new Guid("88888888-8888-8888-8888-888888888888")), clock);

        spaces.Add(remoteSpace);

        var startupSpace = Space.Create(
            new SpaceId(new Guid("dddd4444-dddd-4444-dddd-444444444444")),
            "Startup Garage",
            Currency.GBP,
            new UserId(new Guid("55555555-5555-5555-5555-555555555555")),
            new InviteCode("STRTUP"),
            clock
        );

        startupSpace.Join(new UserId(new Guid("11111111-1111-1111-1111-111111111111")), clock);
        startupSpace.Join(new UserId(new Guid("33333333-3333-3333-3333-333333333333")), clock);
        startupSpace.PromoteMember(new UserId(new Guid("33333333-3333-3333-3333-333333333333")), clock);

        spaces.Add(startupSpace);

        var executiveSpace = Space.Create(
            new SpaceId(new Guid("eeee5555-eeee-5555-eeee-555555555555")),
            "Executive Lounge",
            Currency.USD,
            new UserId(new Guid("11111111-1111-1111-1111-111111111111")),
            new InviteCode("EXEC2K24"),
            clock
        );

        executiveSpace.Join(new UserId(new Guid("22222222-2222-2222-2222-222222222222")), clock);
        executiveSpace.PromoteMember(new UserId(new Guid("22222222-2222-2222-2222-222222222222")), clock);
        executiveSpace.Join(new UserId(new Guid("44444444-4444-4444-4444-444444444444")), clock);
        executiveSpace.PromoteMember(new UserId(new Guid("44444444-4444-4444-4444-444444444444")), clock);

        spaces.Add(executiveSpace);

        // Each space's creator is its treasurer, so settlements can be generated in dev (N7).
        foreach (var space in spaces)
            space.SetTreasurer(space.Members.First(m => m.IsAdmin).UserId);

        return spaces;
    }

    private class FixedClock : IClock
    {
        public DateTime UtcNow { get; }

        public FixedClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }
    }
}
