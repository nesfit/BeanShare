using System.Security.Claims;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.CreateBalanceEntry;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Billing.Commands.CloseBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.CreateBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.OpenBillingPeriod;
using BeanShare.Application.Features.Settlement.Commands.ConfirmPayment;
using BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure.Tests.InMemory;

// T-PLH-10 (X5, Y4) and the race of T-PLH-05 (U6), InMemory variants: a link answer, a merge and a confirm share
// the space and settlement tokens, so the held one loses. InMemory is not atomic (DS-39, F28), so only the outcome
// is asserted here; the end states are asserted by the Npgsql variants in Tests.Integration/Concurrency.
[Collection(InMemoryContainerCollection.Name)]
public sealed class PlaceholderRaceTests
{
    private const string PeteEmail = "pete@example.org";

    private sealed record World(InMemoryContainer Container, UserId Admin, UserId Alice, Guid SpaceId, UserId Placeholder);

    private static async Task<World> ArrangeAsync()
    {
        var container = await InMemoryContainer.CreateAsync();
        var admin = await container.SeedUserAsync("admin@example.org", "Admin");
        var alice = await container.SeedUserAsync("alice@example.org", "Alice");
        var space = await container.SendAsync(admin, new CreateSpaceCommand("Kava", "USD"));
        (await container.SendAsync(alice, new JoinSpaceCommand(space.Value.InviteCode))).IsSuccess.Should().BeTrue();
        var placeholder = await container.SendAsync(admin, new CreatePlaceholderMemberCommand(space.Value.SpaceId, "Old Pete", PeteEmail));
        placeholder.IsSuccess.Should().BeTrue();
        return new World(container, admin, alice, space.Value.SpaceId, new UserId(placeholder.Value.UserId));
    }

    private static ClaimsPrincipal Pete() => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", "idp-pete"), new Claim("name", "Peter"), new Claim(OidcClaims.Email, PeteEmail), new Claim(OidcClaims.EmailVerified, "true")
    }, "oidc"));

    private static async Task<LinkResolveOutcome> ResolveAsync(InMemoryContainer container, bool accept)
    {
        await using var scope = container.Services.CreateAsyncScope();
        var sync = ActivatorUtilities.CreateInstance<UserSynchronizationService>(scope.ServiceProvider);
        return await sync.ResolvePlaceholderLinkAsync(Pete(), accept);
    }

    [Fact(Timeout = 10000)]
    public async Task AHeldAccept_LosingToAMerge_GivesChangedMeanwhile()
    {
        var w = await ArrangeAsync();
        await using var _ = w.Container;

        w.Container.Barrier.Arm();
        var held = Task.Run(() => ResolveAsync(w.Container, accept: true));
        await w.Container.Barrier.WaitUntilHeldAsync();

        (await w.Container.SendAsync(w.Admin, new MergePlaceholderCommand(w.SpaceId, w.Placeholder.Value, w.Alice.Value))).IsSuccess.Should().BeTrue();

        w.Container.Barrier.Release();
        (await held).Should().Be(LinkResolveOutcome.ChangedMeanwhile, "a lost race is an outcome, not an exception");
    }

    [Fact(Timeout = 10000)]
    public async Task AHeldMerge_LosingToAnAccept_GetsConcurrencyConflict()
    {
        var w = await ArrangeAsync();
        await using var _ = w.Container;

        w.Container.Barrier.Arm();
        var held = Task.Run(() => w.Container.SendAsync(w.Admin, new MergePlaceholderCommand(w.SpaceId, w.Placeholder.Value, w.Alice.Value)));
        await w.Container.Barrier.WaitUntilHeldAsync();

        (await ResolveAsync(w.Container, accept: true)).Should().Be(LinkResolveOutcome.Claimed);

        w.Container.Barrier.Release();
        (await held).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    [Fact(Timeout = 10000)]
    public async Task AHeldDecline_LosingToAMerge_GivesChangedMeanwhile()
    {
        var w = await ArrangeAsync();
        await using var _ = w.Container;

        w.Container.Barrier.Arm();
        var held = Task.Run(() => ResolveAsync(w.Container, accept: false));
        await w.Container.Barrier.WaitUntilHeldAsync();

        (await w.Container.SendAsync(w.Admin, new MergePlaceholderCommand(w.SpaceId, w.Placeholder.Value, w.Alice.Value))).IsSuccess.Should().BeTrue();

        w.Container.Barrier.Release();
        (await held).Should().Be(LinkResolveOutcome.ChangedMeanwhile);
    }

    [Fact(Timeout = 10000)]
    public async Task AHeldConfirm_LosingToAMerge_GetsConcurrencyConflict()
    {
        var w = await ArrangeAsync();
        await using var _ = w.Container;
        var today = DateTime.UtcNow.Date;
        var c = DateTime.SpecifyKind(today.AddDays(-10), DateTimeKind.Utc);
        (await w.Container.SendAsync(w.Admin, new UpdateLedgerSettingsCommand(w.SpaceId, w.Admin.Value, c))).IsSuccess.Should().BeTrue();
        (await w.Container.SendAsync(w.Admin, new CreateBalanceEntryCommand(w.SpaceId, w.Placeholder.Value, -50m, c, "Excel cutoff"))).IsSuccess.Should().BeTrue();
        var period = await w.Container.SendAsync(w.Admin, new CreateBillingPeriodCommand(w.SpaceId, "P", c, today.AddDays(-1)));
        (await w.Container.SendAsync(w.Admin, new OpenBillingPeriodCommand(period.Value.Id))).IsSuccess.Should().BeTrue();
        (await w.Container.SendAsync(w.Admin, new CloseBillingPeriodCommand(period.Value.Id))).IsSuccess.Should().BeTrue();
        var settlement = await w.Container.SendAsync(w.Admin, new GenerateSettlementCommand(period.Value.Id));
        settlement.IsSuccess.Should().BeTrue();

        w.Container.Barrier.Arm();
        var held = Task.Run(() => w.Container.SendAsync(w.Admin, new ConfirmPaymentCommand(settlement.Value.Id, w.Placeholder.Value)));
        await w.Container.Barrier.WaitUntilHeldAsync();

        var merge = await w.Container.SendAsync(w.Admin, new MergePlaceholderCommand(w.SpaceId, w.Placeholder.Value, w.Alice.Value));

        w.Container.Barrier.Release();
        var confirm = await held;
        merge.IsSuccess.Should().BeTrue();
        confirm.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }
}
