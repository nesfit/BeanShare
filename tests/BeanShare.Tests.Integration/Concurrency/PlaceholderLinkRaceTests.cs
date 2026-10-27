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
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Tests.Integration.Concurrency;

// The placeholder races on PostgreSQL: a link answer, a merge and a confirm share the space and
// settlement tokens, so exactly one of two commits, with no partial state.
public sealed class PlaceholderLinkRaceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private NpgsqlAppContainer _container = null!;

    public PlaceholderLinkRaceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _container = NpgsqlAppContainer.Create(_fixture.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private sealed record World(UserId Admin, UserId Alice, Guid SpaceId, UserId Placeholder, string Email, string Subject);

    private async Task<World> ArrangeAsync()
    {
        var admin = await _container.SeedUserAsync("Admin");
        var alice = await _container.SeedUserAsync("Alice");
        var space = await _container.SendAsync(admin, new CreateSpaceCommand("Race", "CZK"));
        (await _container.SendAsync(alice, new JoinSpaceCommand(space.Value.InviteCode))).IsSuccess.Should().BeTrue();
        var email = $"{Guid.NewGuid():N}@example.org";
        var placeholder = await _container.SendAsync(admin, new CreatePlaceholderMemberCommand(space.Value.SpaceId, "Old Pete", email));
        placeholder.IsSuccess.Should().BeTrue();
        return new World(admin, alice, space.Value.SpaceId, new UserId(placeholder.Value.UserId), email, $"idp-{Guid.NewGuid():N}");
    }

    private async Task<LinkResolveOutcome> ResolveAsync(World w, bool accept)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", w.Subject), new Claim("name", "Peter"), new Claim(OidcClaims.Email, w.Email), new Claim(OidcClaims.EmailVerified, "true")
        }, "oidc"));
        await using var scope = _container.Services.CreateAsyncScope();
        var sync = ActivatorUtilities.CreateInstance<UserSynchronizationService>(scope.ServiceProvider);
        return await sync.ResolvePlaceholderLinkAsync(principal, accept);
    }

    private Task<Domain.Entities.User> UserAsync(UserId id) =>
        _container.ReadAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == id));

    private static MergePlaceholderCommand Merge(World w) => new(w.SpaceId, w.Placeholder.Value, w.Alice.Value);

    [Fact]
    public async Task AHeldAccept_LosingToAMerge_GivesChangedMeanwhile_AndThePlaceholderStaysMerged()
    {
        var w = await ArrangeAsync();

        _container.Barrier.Arm();
        var held = Task.Run(() => ResolveAsync(w, accept: true));
        await _container.Barrier.WaitUntilHeldAsync();

        (await _container.SendAsync(w.Admin, Merge(w))).IsSuccess.Should().BeTrue();

        _container.Barrier.Release();
        (await held).Should().Be(LinkResolveOutcome.ChangedMeanwhile);
        var pete = await UserAsync(w.Placeholder);
        pete.MergedIntoUserId.Should().Be(w.Alice);
        pete.IsPlaceholder.Should().BeTrue();
        pete.ProviderUserId.Should().BeNull();
    }

    [Fact]
    public async Task AHeldMerge_LosingToAnAccept_GetsConcurrencyConflict_AndThePlaceholderIsClaimed()
    {
        var w = await ArrangeAsync();

        _container.Barrier.Arm();
        var held = Task.Run(() => _container.SendAsync(w.Admin, Merge(w)));
        await _container.Barrier.WaitUntilHeldAsync();

        (await ResolveAsync(w, accept: true)).Should().Be(LinkResolveOutcome.Claimed);

        _container.Barrier.Release();
        (await held).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        var pete = await UserAsync(w.Placeholder);
        pete.IsPlaceholder.Should().BeFalse();
        pete.MergedIntoUserId.Should().BeNull();
        pete.ProviderUserId.Should().Be(w.Subject);
    }

    [Fact]
    public async Task AHeldDecline_LosingToAMerge_GivesChangedMeanwhile_AndCreatesNoAccount()
    {
        var w = await ArrangeAsync();

        _container.Barrier.Arm();
        var held = Task.Run(() => ResolveAsync(w, accept: false));
        await _container.Barrier.WaitUntilHeldAsync();

        (await _container.SendAsync(w.Admin, Merge(w))).IsSuccess.Should().BeTrue();

        _container.Barrier.Release();
        (await held).Should().Be(LinkResolveOutcome.ChangedMeanwhile);
        (await _container.ReadAsync(db => db.Users.AsNoTracking().CountAsync(u => u.Email == w.Email))).Should().Be(0);
        (await UserAsync(w.Placeholder)).MergedIntoUserId.Should().Be(w.Alice);
    }

    [Fact]
    public async Task AHeldConfirm_RacingAMerge_ExactlyOneCommits()
    {
        var w = await ArrangeAsync();
        var today = DateTime.UtcNow.Date;
        var c = DateTime.SpecifyKind(today.AddDays(-10), DateTimeKind.Utc);
        (await _container.SendAsync(w.Admin, new UpdateLedgerSettingsCommand(w.SpaceId, w.Admin.Value, c))).IsSuccess.Should().BeTrue();
        (await _container.SendAsync(w.Admin, new CreateBalanceEntryCommand(w.SpaceId, w.Placeholder.Value, -50m, c, "Excel cutoff"))).IsSuccess.Should().BeTrue();
        var period = await _container.SendAsync(w.Admin, new CreateBillingPeriodCommand(w.SpaceId, "P", c, today.AddDays(-1)));
        (await _container.SendAsync(w.Admin, new OpenBillingPeriodCommand(period.Value.Id))).IsSuccess.Should().BeTrue();
        (await _container.SendAsync(w.Admin, new CloseBillingPeriodCommand(period.Value.Id))).IsSuccess.Should().BeTrue();
        var settlement = await _container.SendAsync(w.Admin, new GenerateSettlementCommand(period.Value.Id));
        settlement.IsSuccess.Should().BeTrue();

        _container.Barrier.Arm();
        var held = Task.Run(() => _container.SendAsync(w.Admin, new ConfirmPaymentCommand(settlement.Value.Id, w.Placeholder.Value)));
        await _container.Barrier.WaitUntilHeldAsync();

        var merge = await _container.SendAsync(w.Admin, Merge(w));

        _container.Barrier.Release();
        var confirm = await held;
        merge.IsSuccess.Should().BeTrue();
        confirm.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        var line = await _container.ReadAsync(db => db.Settlements.AsNoTracking()
            .SelectMany(s => s.Lines).SingleAsync(l => l.UserId == w.Placeholder));
        line.IsConfirmed.Should().BeFalse("the merge won, so the debt moves only by the sweep");
    }
}
