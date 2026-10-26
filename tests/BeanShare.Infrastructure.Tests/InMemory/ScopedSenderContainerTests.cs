using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Spaces.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure.Tests.InMemory;

// T-INF-05: the web's per-operation scopes, in a container built like BlazorWeb's.
[Collection(InMemoryContainerCollection.Name)]
public sealed class ScopedSenderContainerTests
{
    [Fact(Timeout = 10000)]
    public async Task DomainEvents_ArePublishedAndSavedInTheOperationScope()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var alice = await container.SeedUserAsync("a@example.org", "Alice");
        var bob = await container.SeedUserAsync("b@example.org", "Bob");

        var created = await container.SendAsync(alice, new CreateSpaceCommand("Kava", "CZK"));
        created.IsSuccess.Should().BeTrue();

        var joined = await container.SendAsync(bob, new JoinSpaceCommand(created.Value.InviteCode));
        joined.IsSuccess.Should().BeTrue();

        var notificationsForAlice = await container.ReadAsync(db =>
            db.Notifications.AsNoTracking().CountAsync(n => n.UserId == alice));
        notificationsForAlice.Should().Be(1, "the MemberJoined handler ran through IPublisher in Bob's operation scope");
    }

    [Fact(Timeout = 10000)]
    public async Task StaleCircuit_SucceedsFirstTimeAfterAnotherScopeBumpedLedgerVersion()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var alice = await container.SeedUserAsync("a@example.org", "Alice");

        await using var circuit = container.Services.CreateAsyncScope();
        var circuitSender = circuit.ServiceProvider.GetRequiredService<IScopedSender>();
        container.User.Set(alice);
        var spaceId = (await circuitSender.Send(new CreateSpaceCommand("Kava", "CZK"))).Value.SpaceId;

        var bumped = await container.SendAsync(alice, new UpdateLedgerSettingsCommand(spaceId, alice.Value, null));
        bumped.IsSuccess.Should().BeTrue();

        container.User.Set(alice);
        var renamed = await circuitSender.Send(new UpdateSpaceCommand(spaceId, "Kava 2"));

        renamed.IsSuccess.Should().BeTrue();
        renamed.Value.Name.Should().Be("Kava 2");
    }
}
