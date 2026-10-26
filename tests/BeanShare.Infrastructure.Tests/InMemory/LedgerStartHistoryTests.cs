using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.History.Commands.CommitHistoryImport;
using BeanShare.Application.Features.Spaces.Commands;
using FluentAssertions;

namespace BeanShare.Infrastructure.Tests.InMemory;

// DS-37 in both directions, through the pipeline (ported from code-adv-5 A5-3): history must end before C, C cannot
// move into the history, and C cannot be cleared while history exists.
[Collection(InMemoryContainerCollection.Name)]
public sealed class LedgerStartHistoryTests
{
    private static readonly DateTime C = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc);

    private static string D(DateTime date) => date.ToString("yyyy-MM-dd");

    private static string Cups(DateTime start, DateTime end) =>
        $"period;start;end;member;cups\nR1;{D(start)};{D(end)};alice@example.org;40\nR1;{D(start)};{D(end)};tess@example.org;10";

    [Fact(Timeout = 20000)]
    public async Task HistoryMustEndBeforeC_InBothDirections_AndCCannotBeClearedWhileHistoryExists()
    {
        await using var box = await InMemoryContainer.CreateAsync();
        var tess = await box.SeedUserAsync("tess@example.org", "Tess");
        var alice = await box.SeedUserAsync("alice@example.org", "Alice");
        var space = (await box.SendAsync(tess, new CreateSpaceCommand("Kava", "CZK"))).Value;
        (await box.SendAsync(alice, new JoinSpaceCommand(space.InviteCode))).IsSuccess.Should().BeTrue();
        (await box.SendAsync(tess, new UpdateLedgerSettingsCommand(space.SpaceId, tess.Value, C))).IsSuccess.Should().BeTrue();

        (await box.SendAsync(tess, new CommitHistoryImportCommand(space.SpaceId, Cups(C.AddDays(-100), C)))).IsSuccess
            .Should().BeFalse("a period ending on C overlaps the ledger");
        var history = await box.SendAsync(tess, new CommitHistoryImportCommand(space.SpaceId, Cups(C.AddDays(-100), C.AddDays(-1))));
        history.IsSuccess.Should().BeTrue();

        (await box.SendAsync(tess, new UpdateLedgerSettingsCommand(space.SpaceId, null, C.AddDays(-1)))).IsSuccess
            .Should().BeFalse("C must stay after the history");
        (await box.SendAsync(tess, new UpdateLedgerSettingsCommand(space.SpaceId, null, null, ClearLedgerStartDate: true))).IsSuccess
            .Should().BeFalse("without C the history's 'ends before C' rule no longer holds");
        (await box.SendAsync(tess, new UpdateLedgerSettingsCommand(space.SpaceId, null, C.AddDays(1)))).IsSuccess
            .Should().BeTrue("a later C keeps the history before it");
    }

    [Fact(Timeout = 20000)]
    public async Task WithoutHistory_CCanBeCleared()
    {
        await using var box = await InMemoryContainer.CreateAsync();
        var tess = await box.SeedUserAsync("tess@example.org", "Tess");
        var space = (await box.SendAsync(tess, new CreateSpaceCommand("Kava", "CZK"))).Value;
        (await box.SendAsync(tess, new UpdateLedgerSettingsCommand(space.SpaceId, tess.Value, C))).IsSuccess.Should().BeTrue();

        (await box.SendAsync(tess, new UpdateLedgerSettingsCommand(space.SpaceId, null, null, ClearLedgerStartDate: true))).IsSuccess
            .Should().BeTrue();
    }
}
