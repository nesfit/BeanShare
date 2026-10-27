using BeanShare.Application.Common;
using BeanShare.Application.Features.Admin.Users.Commands;
using BeanShare.Application.Features.Balances.Commands.CreateBalanceEntry;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Balances.Queries.GetSpaceBalances;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Domain.Enums;

namespace BeanShare.Infrastructure.Tests.InMemory;

// "Assign to space" (system admin) takes people with an account only. A placeholder in a second space would let a merge
// in its own space move the second space's money to a target who is no member there.
[Collection(InMemoryContainerCollection.Name)]
public sealed class AssignUserToSpaceTests
{
    private static readonly DateTime C = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc);

    [Fact(Timeout = 20000)]
    public async Task APlaceholderOrAMergedUser_CannotBeAssignedToAnotherSpace()
    {
        await using var box = await InMemoryContainer.CreateAsync();
        var ann = await box.SeedUserAsync("ann@example.org", "Ann");
        var bob = await box.SeedUserAsync("bob@example.org", "Bob");
        var cid = await box.SeedUserAsync("cid@example.org", "Cid");
        var spaceA = (await box.SendAsync(ann, new CreateSpaceCommand("Room A", "CZK"))).Value;
        var spaceB = (await box.SendAsync(bob, new CreateSpaceCommand("Room B", "CZK"))).Value;
        (await box.SendAsync(bob, new UpdateLedgerSettingsCommand(spaceB.SpaceId, bob.Value, C))).IsSuccess.Should().BeTrue();
        var pete = (await box.SendAsync(ann, new CreatePlaceholderMemberCommand(spaceA.SpaceId, "Old Pete", "pete@example.org"))).Value;
        var olga = (await box.SendAsync(ann, new CreatePlaceholderMemberCommand(spaceA.SpaceId, "Old Olga", "olga@example.org"))).Value;
        (await box.SendAsync(ann, new MergePlaceholderCommand(spaceA.SpaceId, olga.UserId, ann.Value))).IsSuccess.Should().BeTrue();

        var placeholder = await box.SendAsync(bob, new AssignUserToSpaceCommand(pete.UserId, spaceB.SpaceId));
        var merged = await box.SendAsync(bob, new AssignUserToSpaceCommand(olga.UserId, spaceB.SpaceId));
        var person = await box.SendAsync(bob, new AssignUserToSpaceCommand(cid.Value, spaceB.SpaceId));

        placeholder.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ValidationError);
        merged.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberMerged);
        person.IsSuccess.Should().BeTrue("a person with an account can still be assigned");

        // So no entry in room B can name Pete, and merging him in room A moves nothing there.
        (await box.SendAsync(bob, new CreateBalanceEntryCommand(spaceB.SpaceId, pete.UserId, 100m, C.AddDays(-1), "Excel", BalanceEntryKind.Opening, bob.Value)))
            .IsSuccess.Should().BeFalse();
        (await box.SendAsync(ann, new MergePlaceholderCommand(spaceA.SpaceId, pete.UserId, ann.Value))).IsSuccess.Should().BeTrue();
        (await box.SendAsync(bob, new GetSpaceBalancesQuery(spaceB.SpaceId))).Value.Members.Should().NotContain(m => m.UserId == ann.Value);
    }
}
