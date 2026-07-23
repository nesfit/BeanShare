using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence.Repositories;
using BeanShare.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace BeanShare.Infrastructure.Tests.Repositories;

public sealed class ActiveQrCodeRepositoryTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _databaseFixture;

    public ActiveQrCodeRepositoryTests(DatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    [Fact]
    public async Task GetBySpaceIdAsync_ShouldExcludeDeactivatedCodes()
    {
        var clock = new TestClock();
        var spaceId = new SpaceId(Guid.NewGuid());

        // Each ActiveQrCode owns its own CoffeeProduct (EF owned entity), so build a
        // distinct product per code rather than sharing one instance.
        var active = ActiveQrCode.Create(
            spaceId, "Kitchen", CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso), string.Empty, 0, clock);
        var deactivated = ActiveQrCode.Create(
            spaceId, "Old Meeting Room", CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso), string.Empty, 0, clock);
        deactivated.Deactivate();

        await using (var writeCtx = await _databaseFixture.CreateDbContextAsync())
        {
            var repo = new ActiveQrCodeRepository(writeCtx);
            await repo.AddAsync(active);
            await repo.AddAsync(deactivated);
            await writeCtx.SaveChangesAsync();
        }

        await using var readCtx = await _databaseFixture.CreateDbContextAsync();
        var readRepo = new ActiveQrCodeRepository(readCtx);
        var result = await readRepo.GetBySpaceIdAsync(spaceId);

        result.Should().ContainSingle("deactivated codes must not appear in the active QR list");
        result[0].Id.Should().Be(active.Id);
        result.Should().NotContain(q => q.Id == deactivated.Id);
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2025, 9, 14, 12, 0, 0, DateTimeKind.Utc);
    }
}
