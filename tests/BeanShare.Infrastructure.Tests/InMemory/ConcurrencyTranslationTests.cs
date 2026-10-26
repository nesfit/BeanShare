using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure.Tests.InMemory;

// T-INF-09: a version mismatch is translated, never a 500, and the scope stays usable.
[Collection(InMemoryContainerCollection.Name)]
public sealed class ConcurrencyTranslationTests
{
    [Fact(Timeout = 10000)]
    public async Task VersionMismatch_IsAConcurrencyConflict_AndTheSameScopeRecovers()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var alice = await container.SeedUserAsync("a@example.org", "Alice");
        var spaceId = (await container.SendAsync(alice, new CreateSpaceCommand("Kava", "CZK"))).Value.SpaceId;

        await using var scope = container.Services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        container.User.Set(alice);
        container.Barrier.Arm();
        var held = Task.Run(() => mediator.Send(new UpdateSpaceCommand(spaceId, "Renamed")));
        await container.Barrier.WaitUntilHeldAsync();

        var winner = await container.SendAsync(alice, new UpdateLedgerSettingsCommand(spaceId, alice.Value, null));
        winner.IsSuccess.Should().BeTrue();

        container.Barrier.Release();
        var loser = await held;

        loser.IsFailure.Should().BeTrue();
        loser.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        scope.ServiceProvider.GetRequiredService<BeanShareDbContext>().ChangeTracker.Entries().Should().BeEmpty();

        var retried = await mediator.Send(new UpdateSpaceCommand(spaceId, "Renamed"));
        retried.IsSuccess.Should().BeTrue();
    }

    [Fact(Timeout = 10000)]
    public async Task CommitPathSave_IsTranslatedToo()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var alice = await container.SeedUserAsync("a@example.org", "Alice");
        var spaceId = (await container.SendAsync(alice, new CreateSpaceCommand("Kava", "CZK"))).Value.SpaceId;

        await using var scope = container.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BeanShareDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var space = await db.Spaces.Include(s => s.Members).SingleAsync(s => s.Id == new Domain.ValueObjects.SpaceId(spaceId));

        await container.SendAsync(alice, new UpdateLedgerSettingsCommand(spaceId, alice.Value, null));

        space.UpdateName("Stale rename");
        await unitOfWork.BeginTransactionAsync();
        var commit = () => unitOfWork.CommitTransactionAsync();

        var conflict = (await commit.Should().ThrowAsync<ConcurrencyConflictException>()).Which;
        conflict.EntityTypes.Should().Contain(typeof(Space));
        db.ChangeTracker.Entries().Should().BeEmpty();
    }
}
