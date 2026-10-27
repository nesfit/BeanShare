using System.Security.Claims;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Features.Users.Queries.GetPendingPlaceholderLink;
using BeanShare.Application.Services;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Users;

// The pending person reads its link summary through the real pipeline, and nothing else.
public sealed class PendingLinkPipelineTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly ICurrentPrincipalAccessor _principal = Substitute.For<ICurrentPrincipalAccessor>();
    private readonly IUserContext _pendingContext = Substitute.For<IUserContext>();

    public PendingLinkPipelineTests()
    {
        var placeholder = _world.AddPlaceholder("Old Pete", "pete@example.org");
        _world.Space.SetTreasurer(_world.Admin);
        _world.AddEntry(placeholder, -20m, Jan1);
        _world.SpaceRepository.GetBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<Space>)new[] { _world.Space }.Where(ci.Arg<ISpec<Space>>().Criteria.Compile()).ToList());
        // The pending person has no user row: its id from the IdP is unknown to the repository.
        _pendingContext.CurrentUserId.Returns(UserId.New());
        _pendingContext.Email.Returns("pete@example.org");
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddScopedSender();
        services.AddScoped(_ => _pendingContext);
        services.AddScoped(_ => _principal);
        services.AddScoped(_ => _world.UserRepository);
        services.AddScoped(_ => _world.SpaceRepository);
        services.AddScoped(_ => _world.ConsumptionRepository);
        services.AddScoped(_ => _world.BalanceEntryRepository);
        services.AddScoped(_ => _world.SettlementRepository);
        services.AddScoped(_ => _world.CoffeeStockRepository);
        services.AddScoped(_ => _world.BillingPeriodRepository);
        services.AddScoped(_ => _world.UserService);
        services.AddScoped<IClock>(_ => _world.Clock);
        services.AddScoped(_ => Substitute.For<IUnitOfWork>());
        services.AddScoped(_ => Substitute.For<IDomainEventDispatcher>());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private void GivenPrincipal(bool pending, string verified)
    {
        var claims = new List<Claim> { new(OidcClaims.Email, "Pete@Example.org"), new(OidcClaims.EmailVerified, verified) };
        if (pending)
            claims.Add(new Claim(OidcClaims.LinkPending, "true"));
        _principal.Principal.Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc")));
    }

    private async Task<T> SendAsync<T>(MediatR.IRequest<T> request)
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IScopedSender>().Send(request);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public async Task APendingVerifiedPrincipal_GetsTheSummary(string verified)
    {
        GivenPrincipal(pending: true, verified);

        var summary = await SendAsync(new GetPendingPlaceholderLinkQuery());

        summary.Should().NotBeNull();
        summary!.PlaceholderName.Should().Be("Old Pete");
        summary.Email.Should().Be("pete@example.org");
        summary.Spaces.Should().ContainSingle().Which.Should().Match<PendingPlaceholderSpaceDto>(s =>
            s.SpaceId == _world.Space.Id.Value && s.SpaceName == "Kava" && s.Currency == "CZK" && s.Balance == -20m);
    }

    [Fact]
    public async Task ThePendingPrincipal_CannotCreateASpace()
    {
        GivenPrincipal(pending: true, "true");

        var result = await SendAsync(new CreateSpaceCommand("Mine", "CZK"));

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.Unauthenticated);
        await _world.SpaceRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Theory]
    [InlineData(false, "true")]
    [InlineData(true, "false")]
    [InlineData(true, "yes")]
    public async Task WithoutThePendingClaimOrAVerifiedEmail_TheQueryIsEmpty(bool pending, string verified)
    {
        GivenPrincipal(pending, verified);

        (await SendAsync(new GetPendingPlaceholderLinkQuery())).Should().BeNull();
    }

    [Fact]
    public async Task AMergedPlaceholder_IsNeverOffered()
    {
        GivenPrincipal(pending: true, "true");
        var alice = _world.AddMember("Alice");
        var placeholder = _world.Space.Members.Select(m => _world.UserOf(m.UserId)).Single(u => u.IsPlaceholder).Id;
        (await _world.MergeAsync(placeholder, alice)).IsSuccess.Should().BeTrue();

        (await SendAsync(new GetPendingPlaceholderLinkQuery())).Should().BeNull();
    }
}
