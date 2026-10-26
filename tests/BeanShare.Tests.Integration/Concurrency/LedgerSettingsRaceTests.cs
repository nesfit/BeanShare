using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using BeanShare.Tests.Integration.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Tests.Integration.Concurrency;

// T-DOM-04 (Npgsql variant): SetTreasurer and Demote both bump Space.LedgerVersion, so a racing pair
// never leaves a non-admin treasurer (DS-28).
public sealed class LedgerSettingsRaceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private NpgsqlAppContainer _container = null!;

    public LedgerSettingsRaceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _container = NpgsqlAppContainer.Create(_fixture.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task SetTreasurerHeld_RacingDemote_TheHeldOneGets409()
    {
        var admin = await _container.SeedUserAsync("Admin");
        var member = await _container.SeedUserAsync("Member");
        var created = (await _container.SendAsync(admin, new CreateSpaceCommand("Race", "CZK"))).Value;
        (await _container.SendAsync(member, new JoinSpaceCommand(created.InviteCode))).IsSuccess.Should().BeTrue();
        (await _container.SendAsync(admin, new PromoteMemberCommand(created.SpaceId, member.Value))).IsSuccess.Should().BeTrue();

        _container.Barrier.Arm();
        var held = Task.Run(() => _container.SendAsync(admin, new UpdateLedgerSettingsCommand(created.SpaceId, member.Value, null)));
        await _container.Barrier.WaitUntilHeldAsync();

        (await _container.SendAsync(admin, new DemoteMemberCommand(created.SpaceId, member.Value))).IsSuccess.Should().BeTrue();

        _container.Barrier.Release();
        var loser = await held;

        loser.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        var space = await _container.ReadAsync(db => db.Spaces.AsNoTracking().Include(s => s.Members)
            .SingleAsync(s => s.Id == new SpaceId(created.SpaceId)));
        space.TreasurerUserId.Should().BeNull();
        space.GetMember(member)!.Role.Should().Be(SpaceRole.Member);
    }
}
