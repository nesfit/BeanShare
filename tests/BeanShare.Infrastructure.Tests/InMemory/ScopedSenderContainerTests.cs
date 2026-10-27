using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Balances.Queries.GetSpaceBalances;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Spaces.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure.Tests.InMemory;

// The web's per-operation scopes, in a container built like BlazorWeb's.
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

    [Fact(Timeout = 10000)]
    public async Task StaleCircuit_SeesALedgerStartSetByAnotherScope()
    {
        await using var container = await InMemoryContainer.CreateAsync();
        var scenario = await LedgerScenario.CreateAsync(container);

        await using var circuit = container.Services.CreateAsyncScope();
        var circuitSender = circuit.ServiceProvider.GetRequiredService<IScopedSender>();
        container.User.Set(scenario.Admin);
        await circuitSender.Send(new GetSpaceBalancesQuery(scenario.SpaceId));

        var start = LedgerScenario.Today.AddDays(-3);
        (await container.SendAsync(scenario.Admin, new UpdateLedgerSettingsCommand(scenario.SpaceId, scenario.Admin.Value, start)))
            .IsSuccess.Should().BeTrue();

        container.User.Set(scenario.Admin);
        var cup = await circuitSender.Send(new RecordConsumptionCommand(
            scenario.SpaceId, LedgerScenario.ProductName, LedgerScenario.ProductBrand, "Filter", 10m, start.AddDays(-1)));

        cup.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.BeforeLedgerStart);
    }
}
