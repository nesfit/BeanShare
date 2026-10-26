using System.Security.Claims;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace BeanShare.Infrastructure.Tests.Identity;

// T-PLH-03 (D24, DS-15): the email fallback never maps to a placeholder and needs a verified email.
public sealed class OidcClaimsTransformationTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly User _anna = User.CreateWithIdAndPassword(Guid.NewGuid(), "anna@example.org", "Anna", "hash", Now);
    private readonly User _placeholder = User.CreatePlaceholder("Old Pete", "pete@example.org", Now);

    public OidcClaimsTransformationTests()
    {
        _userService.GetByProviderUserIdAsync(Arg.Any<AuthenticationProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userService.GetByEmailAsync("anna@example.org", Arg.Any<CancellationToken>()).Returns(_anna);
        _userService.GetByEmailAsync("pete@example.org", Arg.Any<CancellationToken>()).Returns(_placeholder);
    }

    private Task<ClaimsPrincipal> TransformAsync(string email, string? verified, bool requireVerified = true)
    {
        var claims = new List<Claim> { new("sub", "opaque-subject"), new("email", email) };
        if (verified is not null)
            claims.Add(new Claim("email_verified", verified));
        var transformation = new OidcClaimsTransformation(
            _userService, new MemoryCache(new MemoryCacheOptions()), new UserSyncSettings { RequireVerifiedEmailForLinking = requireVerified });
        return transformation.TransformAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc")));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public async Task AVerifiedEmail_MapsToTheNormalAccount(string verified)
    {
        var principal = await TransformAsync("anna@example.org", verified);

        principal.FindFirst("sub")!.Value.Should().Be(_anna.Id.Value.ToString());
        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(_anna.Id.Value.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task AnUnverifiedEmail_DoesNotMap_WhileTheFlagIsOn(string? verified)
    {
        var principal = await TransformAsync("anna@example.org", verified);

        principal.FindFirst("sub")!.Value.Should().Be("opaque-subject");
        principal.HasClaim("beanshare_transformed", "true").Should().BeTrue();
    }

    [Fact]
    public async Task AnUnverifiedEmail_MapsTheLegacyWay_WithTheFlagOff()
    {
        var principal = await TransformAsync("anna@example.org", "false", requireVerified: false);

        principal.FindFirst("sub")!.Value.Should().Be(_anna.Id.Value.ToString());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task APlaceholder_IsNeverMappedByEmail(string verified, bool requireVerified)
    {
        var principal = await TransformAsync("pete@example.org", verified, requireVerified);

        principal.FindFirst("sub")!.Value.Should().Be("opaque-subject");
        principal.FindFirst(ClaimTypes.NameIdentifier).Should().BeNull();
    }

    [Fact]
    public async Task AMergedPlaceholder_IsNeverMappedByEmail()
    {
        _placeholder.MarkMergedInto(_anna.Id);

        var principal = await TransformAsync("pete@example.org", "true");

        principal.FindFirst("sub")!.Value.Should().Be("opaque-subject");
    }
}
