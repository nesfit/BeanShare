using System.Security.Claims;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanShare.Infrastructure.Tests.InMemory;

// A link answer reads the spaces whose ledger token it bumps before it re-reads the placeholder. A merge or an edit that
// commits while the answer is paused at that read is then seen by the re-check, so the two never both commit: no user
// is left both claimed and merged, a claimed account never takes the address an admin just typed, and a decline never
// overwrites that address. Deterministic: the answer is paused at its spaces read. (InMemory asserts outcomes.)
[Collection(InMemoryContainerCollection.Name)]
public sealed class PlaceholderLinkWindowTests
{
    private const string PeteEmail = "pete@example.org";
    private const string TypedEmail = "someone.else@example.org";

    private sealed class PausingSpaces(ISpaceRepository inner) : ISpaceRepository
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Go { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<Space>> GetBySpecAsync(ISpec<Space> specification, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Go.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return await inner.GetBySpecAsync(specification, cancellationToken);
        }

        public Task<Space?> GetByIdAsync(SpaceId id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(id, cancellationToken);
        public Task<Space?> GetSingleBySpecAsync(ISpec<Space> specification, CancellationToken cancellationToken = default) => inner.GetSingleBySpecAsync(specification, cancellationToken);
        public Task AddAsync(Space space, CancellationToken cancellationToken = default) => inner.AddAsync(space, cancellationToken);
        public Task UpdateAsync(Space space, CancellationToken cancellationToken = default) => inner.UpdateAsync(space, cancellationToken);
        public Task<int> GetUserSpaceCountAsync(UserId userId, CancellationToken cancellationToken = default) => inner.GetUserSpaceCountAsync(userId, cancellationToken);
        public Task<Dictionary<UserId, int>> GetSpaceCountsForUsersAsync(IEnumerable<UserId> userIds, CancellationToken cancellationToken = default) => inner.GetSpaceCountsForUsersAsync(userIds, cancellationToken);
    }

    private static ClaimsPrincipal Pete() => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", "idp-pete"), new Claim("name", "Peter"), new Claim(OidcClaims.Email, PeteEmail), new Claim(OidcClaims.EmailVerified, "true")
    }, "oidc"));

    private static async Task<(InMemoryContainer Box, UserId Admin, UserId Alice, Guid SpaceId, UserId Pete)> ArrangeAsync()
    {
        var box = await InMemoryContainer.CreateAsync();
        var admin = await box.SeedUserAsync("admin@example.org", "Admin");
        var alice = await box.SeedUserAsync("alice@example.org", "Alice");
        var space = (await box.SendAsync(admin, new CreateSpaceCommand("Kava", "USD"))).Value;
        (await box.SendAsync(alice, new JoinSpaceCommand(space.InviteCode))).IsSuccess.Should().BeTrue();
        var pete = (await box.SendAsync(admin, new CreatePlaceholderMemberCommand(space.SpaceId, "Old Pete", PeteEmail))).Value;
        return (box, admin, alice, space.SpaceId, new UserId(pete.UserId));
    }

    private static async Task<LinkResolveOutcome> AnswerPausedAsync(InMemoryContainer box, bool accept, Func<Task> inTheWindow)
    {
        await using var scope = box.Services.CreateAsyncScope();
        var pausing = new PausingSpaces(scope.ServiceProvider.GetRequiredService<ISpaceRepository>());
        var sync = ActivatorUtilities.CreateInstance<UserSynchronizationService>(scope.ServiceProvider, pausing);
        var answer = Task.Run(() => sync.ResolvePlaceholderLinkAsync(Pete(), accept));
        await pausing.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await inTheWindow();
        pausing.Go.SetResult();
        return await answer;
    }

    private static Task<BeanShare.Domain.Entities.User> PeteAsync(InMemoryContainer box, UserId pete) =>
        box.ReadAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == pete));

    [Fact(Timeout = 20000)]
    public async Task AnAccept_PausedWhileAMergeCommits_LosesAndLeavesThePlaceholderMerged()
    {
        var (box, admin, alice, spaceId, pete) = await ArrangeAsync();
        await using var _ = box;

        var outcome = await AnswerPausedAsync(box, accept: true, async () =>
            (await box.SendAsync(admin, new MergePlaceholderCommand(spaceId, pete.Value, alice.Value))).IsSuccess.Should().BeTrue());

        outcome.Should().Be(LinkResolveOutcome.ChangedMeanwhile);
        var user = await PeteAsync(box, pete);
        user.IsPlaceholder.Should().BeTrue("the claim did not commit");
        user.MergedIntoUserId.Should().Be(alice);
    }

    [Fact(Timeout = 20000)]
    public async Task AnAccept_PausedWhileAnEditCommits_LosesAndKeepsTheTypedAddressOnThePlaceholder()
    {
        var (box, admin, _, spaceId, pete) = await ArrangeAsync();
        await using var _box = box;

        var outcome = await AnswerPausedAsync(box, accept: true, async () =>
            (await box.SendAsync(admin, new UpdatePlaceholderMemberCommand(spaceId, pete.Value, "Old Pete", TypedEmail))).IsSuccess.Should().BeTrue());

        outcome.Should().Be(LinkResolveOutcome.ChangedMeanwhile);
        var user = await PeteAsync(box, pete);
        user.IsPlaceholder.Should().BeTrue();
        user.Email.Should().Be(TypedEmail);
    }

    [Fact(Timeout = 20000)]
    public async Task ADecline_PausedWhileAnEditCommits_LosesAndDoesNotOverwriteTheTypedAddress()
    {
        var (box, admin, _, spaceId, pete) = await ArrangeAsync();
        await using var _box = box;

        var outcome = await AnswerPausedAsync(box, accept: false, async () =>
            (await box.SendAsync(admin, new UpdatePlaceholderMemberCommand(spaceId, pete.Value, "Old Pete", TypedEmail))).IsSuccess.Should().BeTrue());

        outcome.Should().Be(LinkResolveOutcome.ChangedMeanwhile);
        (await PeteAsync(box, pete)).Email.Should().Be(TypedEmail);
    }
}
