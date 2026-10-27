using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Domain.Common;

namespace BeanShare.Infrastructure.Tests.InMemory;

// A cup racing the drinker's departure: a new cup shares the space's ledger token with Leave and Remove, so whichever
// is held loses. A held Leave fails (the member retries and the departure guard then sees the cup); a held cup is retried
// and then refused, because its drinker is no member any more. Either way no ex-member is left owing a cup.
// InMemory asserts outcomes only (it is not atomic).
[Collection(InMemoryContainerCollection.Name)]
public sealed class DepartureRaceTests
{
    private static readonly DateTime C = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc);

    private static async Task<(InMemoryContainer Box, UserId Ann, UserId Ben, Guid SpaceId)> ArrangeAsync()
    {
        var box = await InMemoryContainer.CreateAsync();
        var ann = await box.SeedUserAsync("ann@example.org", "Ann");
        var ben = await box.SeedUserAsync("ben@example.org", "Ben");
        var space = (await box.SendAsync(ann, new CreateSpaceCommand("Kava", "CZK"))).Value;
        (await box.SendAsync(ben, new JoinSpaceCommand(space.InviteCode))).IsSuccess.Should().BeTrue();
        (await box.SendAsync(ann, new AddStockPurchaseCommand(space.SpaceId, "Alfa", "Roaster", "Filter", 1000m, 500m, "CZK", "Shop", C.AddDays(1)))).IsSuccess.Should().BeTrue();
        (await box.SendAsync(ann, new UpdateLedgerSettingsCommand(space.SpaceId, ann.Value, C))).IsSuccess.Should().BeTrue();
        return (box, ann, ben, space.SpaceId);
    }

    [Fact(Timeout = 20000)]
    public async Task AHeldLeave_LosingToACupForTheLeaver_Fails()
    {
        var (box, ann, ben, spaceId) = await ArrangeAsync();
        await using var _ = box;

        box.Barrier.Arm();
        var leave = Task.Run(() => box.SendAsync(ben, new LeaveSpaceCommand(spaceId)));
        await box.Barrier.WaitUntilHeldAsync();
        (await box.SendAsync(ann, new RecordConsumptionCommand(spaceId, "Alfa", "Roaster", "Filter", 10m, DateTime.UtcNow.AddMinutes(-5), ForUserId: ben.Value)))
            .IsSuccess.Should().BeTrue();
        box.Barrier.Release();

        (await leave).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    [Fact(Timeout = 20000)]
    public async Task AHeldCup_LosingToTheDrinkersLeave_IsRetriedAndRefused()
    {
        var (box, _, ben, spaceId) = await ArrangeAsync();
        await using var _box = box;

        box.Barrier.Arm();
        var cup = Task.Run(() => box.SendAsync(ben, new RecordConsumptionCommand(spaceId, "Alfa", "Roaster", "Filter", 10m, DateTime.UtcNow.AddMinutes(-5))));
        await box.Barrier.WaitUntilHeldAsync();
        (await box.SendAsync(ben, new LeaveSpaceCommand(spaceId))).IsSuccess.Should().BeTrue();
        box.Barrier.Release();

        (await cup).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberNotFound);
    }
}
