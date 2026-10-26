using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Billing.Commands.CloseBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.CreateBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.OpenBillingPeriod;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Domain.Common;
using FluentAssertions;

namespace BeanShare.Tests.Integration.Concurrency;

/// <summary>A space with an admin treasurer, two members and one bag, driven through real commands.</summary>
internal sealed class LedgerScenario
{
    private LedgerScenario(NpgsqlAppContainer container, UserId admin, UserId alice, UserId bob, Guid spaceId)
    {
        Container = container;
        Admin = admin;
        Alice = alice;
        Bob = bob;
        SpaceId = spaceId;
    }

    public NpgsqlAppContainer Container { get; }
    public UserId Admin { get; }
    public UserId Alice { get; }
    public UserId Bob { get; }
    public Guid SpaceId { get; }

    public static DateTime Today => DateTime.UtcNow.Date;

    public static async Task<LedgerScenario> CreateAsync(NpgsqlAppContainer container)
    {
        var admin = await container.SeedUserAsync("Admin");
        var alice = await container.SeedUserAsync("Alice");
        var bob = await container.SeedUserAsync("Bob");
        var created = (await container.SendAsync(admin, new CreateSpaceCommand("Ledger race", "CZK"))).Value;
        (await container.SendAsync(alice, new JoinSpaceCommand(created.InviteCode))).IsSuccess.Should().BeTrue();
        (await container.SendAsync(bob, new JoinSpaceCommand(created.InviteCode))).IsSuccess.Should().BeTrue();
        (await container.SendAsync(admin, new UpdateLedgerSettingsCommand(created.SpaceId, admin.Value, null))).IsSuccess.Should().BeTrue();
        (await container.SendAsync(admin, new AddStockPurchaseCommand(
            created.SpaceId, "Alfa", "Roaster", "Filter", 1000m, 500m, "CZK", "Store", Today.AddDays(-30)))).IsSuccess.Should().BeTrue();
        return new LedgerScenario(container, admin, alice, bob, created.SpaceId);
    }

    public async Task<Guid> LogCupAsync(UserId drinker, DateTime consumedAt)
    {
        var cup = await Container.SendAsync(drinker, new RecordConsumptionCommand(SpaceId, "Alfa", "Roaster", "Filter", 10m, consumedAt));
        cup.IsSuccess.Should().BeTrue();
        return cup.Value.Id;
    }

    public async Task<Guid> ClosedPeriodAsync(DateTime start, DateTime end)
    {
        var period = (await Container.SendAsync(Admin, new CreateBillingPeriodCommand(SpaceId, $"P {start:MMdd}", start, end))).Value;
        (await Container.SendAsync(Admin, new OpenBillingPeriodCommand(period.Id))).IsSuccess.Should().BeTrue();
        (await Container.SendAsync(Admin, new CloseBillingPeriodCommand(period.Id))).IsSuccess.Should().BeTrue();
        return period.Id;
    }
}
