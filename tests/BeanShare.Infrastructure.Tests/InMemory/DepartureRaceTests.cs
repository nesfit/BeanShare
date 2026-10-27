using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.CreateBalanceEntry;
using BeanShare.Application.Features.Balances.Queries.GetSpaceBalances;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using Microsoft.EntityFrameworkCore;

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
    // The other commands that change a member's balance share the token too: a write-off share, a cup edit or delete,
    // and undoing an emptying. With Leave held after its balance check, each racing command commits first, so the held
    // Leave fails and the member stays, owing (or owed) what they owe.

    /// <summary>Ben drank 10 g (5.00) and is owed 5.00 by an opening entry: his balance is 0, so he may leave.</summary>
    private static async Task<(InMemoryContainer Box, UserId Ann, UserId Ben, Guid SpaceId, Guid CupId, Guid BagId)> ArrangeSquareLeaverAsync()
    {
        var (box, ann, ben, spaceId) = await ArrangeAsync();
        var cup = (await box.SendAsync(ben, new RecordConsumptionCommand(spaceId, "Alfa", "Roaster", "Filter", 10m, DateTime.UtcNow.AddMinutes(-30)))).Value;
        (await box.SendAsync(ann, new CreateBalanceEntryCommand(spaceId, ben.Value, 5m, C.AddDays(-1), "Excel", BalanceEntryKind.Opening, ann.Value))).IsSuccess.Should().BeTrue();
        (await BalanceOfAsync(box, ann, spaceId, ben)).Should().Be(0m);
        var bagId = await box.ReadAsync(async db => (await db.CoffeeStocks.AsNoTracking().Include(s => s.Purchases).SingleAsync()).Purchases.Single().Id);
        return (box, ann, ben, spaceId, cup.Id, bagId);
    }

    private static async Task<decimal?> BalanceOfAsync(InMemoryContainer box, UserId admin, Guid spaceId, UserId user) =>
        (await box.SendAsync(admin, new GetSpaceBalancesQuery(spaceId))).Value.Members.SingleOrDefault(m => m.UserId == user.Value)?.Balance;

    private static async Task<bool> LeaveRacingAsync(InMemoryContainer box, UserId ben, Guid spaceId, Func<Task<bool>> other)
    {
        box.Barrier.Arm();
        var leave = Task.Run(() => box.SendAsync(ben, new LeaveSpaceCommand(spaceId)));
        await box.Barrier.WaitUntilHeldAsync();
        (await other()).Should().BeTrue("the racing command commits first");
        box.Barrier.Release();
        var left = await leave;
        left.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        return left.IsSuccess;
    }

    [Fact(Timeout = 20000)]
    public async Task AHeldLeave_LosingToAWriteOffShare_Fails()
    {
        var (box, ann, ben, spaceId, _, bagId) = await ArrangeSquareLeaverAsync();
        await using var _ = box;
        await LeaveRacingAsync(box, ben, spaceId, async () => (await box.SendAsync(ann, new MarkBagEmptyCommand(spaceId, bagId))).IsSuccess);
    }

    [Fact(Timeout = 20000)]
    public async Task AHeldLeave_LosingToASplitOfTheRemainingStock_Fails()
    {
        var (box, ann, ben, spaceId, _, _) = await ArrangeSquareLeaverAsync();
        await using var _ = box;
        var levelId = (await box.SendAsync(ann, new BeanShare.Application.Features.CoffeeStock.Queries.GetSpaceStockQuery(spaceId, 1, 50))).Value.StockLevels.Single().Id;
        await LeaveRacingAsync(box, ben, spaceId, async () => (await box.SendAsync(ann, new SplitRemainingStockCommand(spaceId, levelId))).IsSuccess);
    }

    [Fact(Timeout = 20000)]
    public async Task AHeldLeave_LosingToAnEditOfTheLeaversCup_Fails()
    {
        var (box, ann, ben, spaceId, cupId, _) = await ArrangeSquareLeaverAsync();
        await using var _ = box;
        await LeaveRacingAsync(box, ben, spaceId, async () =>
            (await box.SendAsync(ann, new UpdateConsumptionCommand(cupId, spaceId, "Alfa", "Roaster", "Filter", 30m, DateTime.UtcNow.AddMinutes(-30)))).IsSuccess);
    }

    [Fact(Timeout = 20000)]
    public async Task AHeldLeave_LosingToADeleteOfTheLeaversCup_Fails()
    {
        var (box, ann, ben, spaceId, cupId, _) = await ArrangeSquareLeaverAsync();
        await using var _ = box;
        await LeaveRacingAsync(box, ben, spaceId, async () => (await box.SendAsync(ann, new DeleteConsumptionCommand(cupId, spaceId))).IsSuccess);
    }

    [Fact(Timeout = 20000)]
    public async Task AHeldLeave_LosingToAnUndoOfAnEmptying_Fails()
    {
        var (box, ann, ben, spaceId, _, bagId) = await ArrangeSquareLeaverAsync();
        await using var _ = box;
        // Emptied first: Ben's share of the bag is billed, and an entry squares him again.
        (await box.SendAsync(ann, new MarkBagEmptyCommand(spaceId, bagId))).IsSuccess.Should().BeTrue();
        var owed = -(await BalanceOfAsync(box, ann, spaceId, ben))!.Value;
        (await box.SendAsync(ann, new CreateBalanceEntryCommand(spaceId, ben.Value, owed, C.AddDays(-1), "Square", BalanceEntryKind.Opening, ann.Value))).IsSuccess.Should().BeTrue();
        (await BalanceOfAsync(box, ann, spaceId, ben)).Should().Be(0m);

        await LeaveRacingAsync(box, ben, spaceId, async () => (await box.SendAsync(ann, new UndoBagEmptyCommand(spaceId, bagId))).IsSuccess);
    }
}
