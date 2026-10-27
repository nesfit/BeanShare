using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Balances.Queries.GetSpaceBalances;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.Presets.Commands;
using BeanShare.Application.Features.Spaces.Commands;

namespace BeanShare.Infrastructure.Tests.InMemory;

// A preset cup is billed like an ordinary cup: only to a current member of the space, through the real pipeline.
// Otherwise any member could put a debt on someone who has left (past the departure guard) or on an arbitrary user id,
// and that person would then receive the settlement emails and their report.
[Collection(InMemoryContainerCollection.Name)]
public sealed class PresetCupMembershipTests
{
    private static readonly DateTime C = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc);

    [Fact(Timeout = 20000)]
    public async Task APresetCup_ForSomeoneWhoIsNotAMember_IsRefusedLikeAnOrdinaryCup()
    {
        await using var box = await InMemoryContainer.CreateAsync();
        var ann = await box.SeedUserAsync("ann@example.org", "Ann");
        var ben = await box.SeedUserAsync("ben@example.org", "Ben");
        var space = (await box.SendAsync(ann, new CreateSpaceCommand("Kava", "CZK"))).Value;
        (await box.SendAsync(ben, new JoinSpaceCommand(space.InviteCode))).IsSuccess.Should().BeTrue();
        (await box.SendAsync(ann, new AddStockPurchaseCommand(space.SpaceId, "Alfa", "Roaster", "Filter", 1000m, 500m, "CZK", "Shop", C.AddDays(1)))).IsSuccess.Should().BeTrue();
        (await box.SendAsync(ann, new UpdateLedgerSettingsCommand(space.SpaceId, ann.Value, C))).IsSuccess.Should().BeTrue();
        (await box.SendAsync(ben, new LeaveSpaceCommand(space.SpaceId))).IsSuccess.Should().BeTrue();

        var plain = await box.SendAsync(ann, new RecordConsumptionCommand(space.SpaceId, "Alfa", "Roaster", "Filter", 10m, DateTime.UtcNow.AddMinutes(-5), ForUserId: ben.Value));
        var preset = await box.SendAsync(ann, new CreatePresetCommand(space.SpaceId, "Filtr", "Filter", "Pour over", 10m, null, true));
        preset.IsSuccess.Should().BeTrue();
        var forBen = await box.SendAsync(ann, new RecordConsumptionFromPresetCommand(space.SpaceId, preset.Value.PresetId, null, DateTime.UtcNow.AddMinutes(-5), ben.Value));
        var forNobody = await box.SendAsync(ann, new RecordConsumptionFromPresetCommand(space.SpaceId, preset.Value.PresetId, null, DateTime.UtcNow.AddMinutes(-4), Guid.NewGuid()));
        var forAnn = await box.SendAsync(ann, new RecordConsumptionFromPresetCommand(space.SpaceId, preset.Value.PresetId, null, DateTime.UtcNow.AddMinutes(-3), null));

        plain.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberNotFound);
        forBen.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberNotFound);
        forNobody.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberNotFound);
        forAnn.IsSuccess.Should().BeTrue("a member's own preset cup is unaffected");
        var balances = (await box.SendAsync(ann, new GetSpaceBalancesQuery(space.SpaceId))).Value;
        balances.Members.SingleOrDefault(m => m.UserId == ben.Value)?.Balance.Should().Be(0m);
    }
}
