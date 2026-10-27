using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Settlement;

// A merged placeholder removed from the space must not fall out of the merged map: the map is built from every user
// in the calculation inputs, not from the members.
public sealed class MergedMapLoaderTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan31 = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb1 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb10 = new(2026, 2, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AMergedUserNoLongerAMember_WithACarriedLineAndACup_IsStillSweptAtTheNextGeneration()
    {
        var world = new LedgerWorld("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
        var alice = world.AddMember("Alice");
        var placeholder = world.AddPlaceholder("Old Pete");
        world.Space.SetTreasurer(world.Admin);
        world.AddEntry(placeholder, -50m, Jan1);
        await world.GenerateAsync(world.AddPeriod("January", Jan1, Jan31));

        (await world.MergeAsync(placeholder, alice)).IsSuccess.Should().BeTrue();
        var bag = world.AddBag(1000m, 500m, Feb1);
        world.Drink(placeholder, 20m, Feb10, bag);
        // The emulated stranding: the merged user drops out of the membership (the guard normally forbids it).
        world.Space.RemoveMember(placeholder, world.Clock);
        world.Space.HasMember(placeholder).Should().BeFalse();

        var february = world.AddPeriod("February", Feb1, Feb28);
        (await world.GenerateAsync(february)).IsSuccess.Should().BeTrue();

        var s2 = world.SettlementOf(february);
        var line = s2.GetLineForUser(placeholder)!;
        line.NetAmount.Should().Be(0m);
        line.MergeTransferAmount.Should().Be(60m);
        line.Resolution.Should().Be(LineResolution.Confirmed);
        s2.GetLineForUser(alice)!.NetAmount.Should().Be(-60m);
        s2.Lines.Sum(l => l.NetAmount ?? 0m).Should().Be(0m);

        await world.UserRepository.Received().GetMergedMapAsync(
            Arg.Is<IReadOnlyCollection<UserId>>(ids => ids.Contains(placeholder)), Arg.Any<CancellationToken>());
    }
}
