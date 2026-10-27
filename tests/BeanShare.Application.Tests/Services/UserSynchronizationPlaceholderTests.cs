using System.Security.Claims;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Services;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BeanShare.Application.Tests.Services;

// The sync outcome matrix and the link answer.
public sealed class UserSynchronizationPlaceholderTests
{
    private const string Subject = "idp-subject-1";

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly Dictionary<UserId, User> _users = new();
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IUserRepository _userRepository;
    private readonly UserId _placeholder;

    public UserSynchronizationPlaceholderTests()
    {
        _userRepository = TestUsers.RepositoryOver(_users);
        var placeholder = User.CreatePlaceholder("Old Pete", "pete@example.org", _world.Clock.UtcNow);
        _users[placeholder.Id] = placeholder;
        _placeholder = placeholder.Id;
        _world.Space.AddPlaceholderMember(_placeholder, _world.Clock);

        var normal = User.CreateWithIdAndPassword(Guid.NewGuid(), "anna@example.org", "Anna", "hash", _world.Clock.UtcNow);
        _users[normal.Id] = normal;

        _userService.GetByProviderUserIdAsync(Arg.Any<AuthenticationProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _world.SpaceRepository.GetBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<Space>)new[] { _world.Space }.Where(ci.Arg<ISpec<Space>>().Criteria.Compile()).ToList());
    }

    private UserSynchronizationService Service(bool requireVerified = true) => new(
        _userService, _userRepository, _world.SpaceRepository, _world.NotificationRepository, _unitOfWork, _world.Clock,
        NullLogger<UserSynchronizationService>.Instance, new UserSyncSettings { RequireVerifiedEmailForLinking = requireVerified });

    private static ClaimsPrincipal Principal(string? email, string? verified, string? preferredUsername = null)
    {
        var claims = new List<Claim> { new("sub", Subject), new("name", "Peter Real") };
        if (email is not null)
            claims.Add(new Claim(OidcClaims.Email, email));
        if (verified is not null)
            claims.Add(new Claim(OidcClaims.EmailVerified, verified));
        if (preferredUsername is not null)
            claims.Add(new Claim("preferred_username", preferredUsername));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc"));
    }

    private User Placeholder => _users[_placeholder];

    private User Anna => _users.Values.Single(u => u.Name == "Anna");

    private async Task NothingCreatedAsync()
    {
        await _userService.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _userRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public async Task AVerifiedMatchOnAPlaceholder_IsPending(string verified)
    {
        var outcome = await Service().SyncFromClaimsAsync(Principal("PETE@example.org", verified));

        outcome.Should().Be(new UserSyncOutcome.PlaceholderLinkPending(_placeholder.Value, "Old Pete"));
        Placeholder.IsPlaceholder.Should().BeTrue();
        await NothingCreatedAsync();
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("False")]
    [InlineData("false")]
    [InlineData("yes")]
    public async Task AnUnverifiedMatchOnAPlaceholder_IsRefused_EvenWithTheFlagOff(string? verified)
    {
        foreach (var requireVerified in new[] { true, false })
        {
            var outcome = await Service(requireVerified).SyncFromClaimsAsync(Principal("pete@example.org", verified));
            outcome.Should().Be(new UserSyncOutcome.Refused(Error.Codes.EmailNotVerified));
        }

        await NothingCreatedAsync();
    }

    [Fact]
    public async Task ASentinelEmail_IsRefused()
    {
        var outcome = await Service().SyncFromClaimsAsync(Principal(User.SentinelEmailFor(_placeholder), "true"));

        outcome.Should().Be(new UserSyncOutcome.Refused(Error.Codes.EmailNotVerified));
        await NothingCreatedAsync();
    }

    [Fact]
    public async Task PreferredUsername_IsNeverUsedForMatching()
    {
        var act = () => Service().SyncFromClaimsAsync(Principal(null, "true", preferredUsername: "pete@example.org"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        await NothingCreatedAsync();
    }

    [Fact]
    public async Task ANormalAccount_WithAVerifiedEmail_GetsTheProviderLink()
    {
        var outcome = await Service().SyncFromClaimsAsync(Principal("Anna@Example.org", "true"));

        outcome.Should().BeOfType<UserSyncOutcome.Synced>().Which.User.Should().BeSameAs(Anna);
        Anna.ProviderUserId.Should().Be(Subject);
        Anna.Provider.Should().Be(AuthenticationProvider.Oidc);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _userService.Received().Evict(Anna.Id, Arg.Any<string[]>());
        await NothingCreatedAsync();
    }

    [Fact]
    public async Task ANormalAccount_WithAnUnverifiedEmail_IsRefusedWithTheFlagOn_AndMatchedTheLegacyWayWithItOff()
    {
        (await Service(requireVerified: true).SyncFromClaimsAsync(Principal("anna@example.org", "false")))
            .Should().Be(new UserSyncOutcome.Refused(Error.Codes.EmailNotVerified));

        var legacy = await Service(requireVerified: false).SyncFromClaimsAsync(Principal("anna@example.org", "false"));

        legacy.Should().BeOfType<UserSyncOutcome.Synced>().Which.User.Should().BeSameAs(Anna);
        Anna.ProviderUserId.Should().BeNull("an unverified email is never linked");
        await NothingCreatedAsync();
    }

    [Fact]
    public async Task AMergedPlaceholderWithAMatchingEmail_IsNeverPending_AndCannotBeResolved()
    {
        // Only possible if the email were not released; the merged flag alone must keep it out.
        Placeholder.MarkMergedInto(_world.Admin);

        var outcome = await Service().SyncFromClaimsAsync(Principal("pete@example.org", "true"));
        outcome.Should().NotBeOfType<UserSyncOutcome.PlaceholderLinkPending>();

        (await Service().ResolvePlaceholderLinkAsync(Principal("pete@example.org", "true"), accept: true))
            .Should().Be(LinkResolveOutcome.ChangedMeanwhile);
        Placeholder.ProviderUserId.Should().BeNull();
        Placeholder.IsPlaceholder.Should().BeTrue();
    }

    [Fact]
    public async Task Accept_ClaimsThePlaceholder_WithTheSameId()
    {
        var version = _world.Space.LedgerVersion;

        var outcome = await Service().ResolvePlaceholderLinkAsync(Principal("pete@example.org", "true"), accept: true);

        outcome.Should().Be(LinkResolveOutcome.Claimed);
        Placeholder.IsPlaceholder.Should().BeFalse();
        Placeholder.ProviderUserId.Should().Be(Subject);
        Placeholder.Provider.Should().Be(AuthenticationProvider.Oidc);
        Placeholder.Email.Should().Be("pete@example.org");
        _world.Space.LedgerVersion.Should().BeGreaterThan(version);
        _world.Notifications.Should().ContainSingle().Which.UserId.Should().Be(_world.Admin);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _userService.Received().Evict(_placeholder, Arg.Any<string[]>());
        await NothingCreatedAsync();

        // The access check reads uncached, so the claimed id can act at once.
        (await _userRepository.GetAccessStatusAsync(_placeholder)).CanAct.Should().BeTrue();
    }

    [Fact]
    public async Task Decline_ReleasesTheEmail_AndCreatesTheAccount_InOneSave()
    {
        var outcome = await Service().ResolvePlaceholderLinkAsync(Principal("pete@example.org", "true"), accept: false);

        outcome.Should().Be(LinkResolveOutcome.Declined);
        Placeholder.IsPlaceholder.Should().BeTrue();
        Placeholder.HasDeliverableEmail.Should().BeFalse();
        var account = _users.Values.Single(u => u.Email == "pete@example.org");
        account.Id.Should().NotBe(_placeholder);
        account.IsPlaceholder.Should().BeFalse();
        account.ProviderUserId.Should().Be(Subject);
        account.Name.Should().Be("Peter Real");
        _world.Notifications.Should().ContainSingle().Which.UserId.Should().Be(_world.Admin);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _userService.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        _userService.Received().Evict(_placeholder, Arg.Is<string[]>(e => e.Contains("pete@example.org") && e.Contains(Placeholder.Email)));
    }

    [Fact]
    public async Task ALostRace_GivesChangedMeanwhile_WithoutThrowing()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException([typeof(Space)], new InvalidOperationException("stale")));

        foreach (var accept in new[] { true, false })
        {
            var outcome = await Service().ResolvePlaceholderLinkAsync(Principal("pete@example.org", "true"), accept);
            outcome.Should().Be(LinkResolveOutcome.ChangedMeanwhile);
        }

        await _userService.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        _userService.DidNotReceiveWithAnyArgs().Evict(default, default!);
    }

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    public async Task AnUnverifiedPrincipal_IsNotPending(string? verified)
    {
        (await Service().ResolvePlaceholderLinkAsync(Principal("pete@example.org", verified), accept: true))
            .Should().Be(LinkResolveOutcome.NotPending);
        Placeholder.IsPlaceholder.Should().BeTrue();
    }
}
