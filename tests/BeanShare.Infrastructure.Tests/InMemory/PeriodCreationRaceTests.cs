using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Billing.Commands.CreateBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.OpenBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.ReopenBillingPeriod;
using BeanShare.Application.Features.Billing.Commands.CloseBillingPeriod;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Domain.Common;

namespace BeanShare.Infrastructure.Tests.InMemory;

// Two creates of the same period (a double submit, two admins), or a create racing a reopen, must not both pass the
// overlap check: overlapping live periods would let the running balance hide cups stamped to one of them. Creating and
// opening a period share the space's ledger token with close, reopen and generation, so the held one loses.
// InMemory asserts outcomes only (it is not atomic); the Npgsql variant in Tests.Integration/Concurrency checks that
// only one period is stored.
[Collection(InMemoryContainerCollection.Name)]
public sealed class PeriodCreationRaceTests
{
    private static readonly DateTime C = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-40), DateTimeKind.Utc);

    private static async Task<(InMemoryContainer Box, UserId Ann, Guid SpaceId)> ArrangeAsync()
    {
        var box = await InMemoryContainer.CreateAsync();
        var ann = await box.SeedUserAsync("ann@example.org", "Ann");
        var space = (await box.SendAsync(ann, new CreateSpaceCommand("Kava", "CZK"))).Value;
        (await box.SendAsync(ann, new UpdateLedgerSettingsCommand(space.SpaceId, ann.Value, C))).IsSuccess.Should().BeTrue();
        return (box, ann, space.SpaceId);
    }

    [Fact(Timeout = 20000)]
    public async Task TwoCreatesOfTheSamePeriod_OnlyOneCommits()
    {
        var (box, ann, spaceId) = await ArrangeAsync();
        await using var _ = box;
        var create = new CreateBillingPeriodCommand(spaceId, "October", C, C.AddDays(20));

        box.Barrier.Arm();
        var held = Task.Run(() => box.SendAsync(ann, create));
        await box.Barrier.WaitUntilHeldAsync();
        (await box.SendAsync(ann, create)).IsSuccess.Should().BeTrue();
        box.Barrier.Release();

        (await held).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    [Fact(Timeout = 20000)]
    public async Task ACreateHeld_RacingAReopenOfTheLastPeriod_Loses()
    {
        var (box, ann, spaceId) = await ArrangeAsync();
        await using var _ = box;
        var september = (await box.SendAsync(ann, new CreateBillingPeriodCommand(spaceId, "September", C, null))).Value.Id;
        (await box.SendAsync(ann, new OpenBillingPeriodCommand(september))).IsSuccess.Should().BeTrue();
        (await box.SendAsync(ann, new CloseBillingPeriodCommand(september))).IsSuccess.Should().BeTrue();

        // The next period is created while the last one is reopened (which makes it open-ended again).
        box.Barrier.Arm();
        var held = Task.Run(() => box.SendAsync(ann, new CreateBillingPeriodCommand(spaceId, "October", DateTime.UtcNow.Date.AddDays(1), null)));
        await box.Barrier.WaitUntilHeldAsync();
        (await box.SendAsync(ann, new ReopenBillingPeriodCommand(september))).IsSuccess.Should().BeTrue();
        box.Barrier.Release();

        (await held).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }
}
