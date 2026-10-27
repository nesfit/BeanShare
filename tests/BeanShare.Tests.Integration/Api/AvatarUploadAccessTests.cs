using System.Reflection;
using BeanShare.Api.Endpoints.Me;
using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Infrastructure.Services;
using FastEndpoints;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace BeanShare.Tests.Integration.Api;

// DS-30 for the avatar upload, which runs no command: an unknown, deactivated or placeholder account gets 401 with the
// code before any file is looked at. Pure unit tests (FastEndpoints' Factory, a stub repository); no database.
[Collection(FastEndpointsFactoryCollection.Name)]
public sealed class AvatarUploadAccessTests
{
    private static readonly UserId Caller = new(Guid.NewGuid());

    public AvatarUploadAccessTests() => FastEndpointsFactoryIsolation.ResetResolver();

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task AnAccountThatCannotAct_Gets401WithItsCode(bool exists, bool isActive, bool isPlaceholder)
    {
        var endpoint = Create(new UserAccessStatus(exists, isActive, isPlaceholder, null));

        await endpoint.HandleAsync(default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(401);
        endpoint.HttpContext.Response.Headers.WWWAuthenticate.ToString().Should().Be("Bearer");
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.Unauthenticated);
    }

    [Fact]
    public async Task AnActiveAccount_GetsPastTheCheck()
    {
        var endpoint = Create(new UserAccessStatus(true, true, false, null));
        endpoint.HttpContext.Request.ContentType = "application/x-www-form-urlencoded";
        endpoint.HttpContext.Request.Body = new MemoryStream();

        await endpoint.HandleAsync(default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(400, "the request carries no file");
        endpoint.ValidationFailures.Should().BeEmpty();
    }

    private static UploadAvatarEndpoint Create(UserAccessStatus status, IUserContext? context = null)
    {
        var users = AccessStatusOnly.For(status);
        return Factory.Create<UploadAvatarEndpoint>(
            context ?? new CallerContext(),
            new AvatarStorageService(new ConfigurationBuilder().Build(), users),
            users);
    }

    // A10-4: a principal whose id claim is missing or not a GUID gets the same 401, not a 500.
    [Fact]
    public async Task APrincipalWithoutAUsableIdClaim_Gets401WithItsCode()
    {
        var endpoint = Create(new UserAccessStatus(true, true, false, null), new NoIdClaimContext());

        await endpoint.HandleAsync(default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(401);
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.Unauthenticated);
    }

    private sealed class NoIdClaimContext : IUserContext
    {
        public UserId CurrentUserId => throw new InvalidOperationException("User ID claim not found or invalid");
        public string Email => "caller@example.org";
        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class CallerContext : IUserContext
    {
        public UserId CurrentUserId => Caller;
        public string Email => "caller@example.org";
        public IReadOnlyCollection<string> Roles => [];
    }

    /// <summary>An <see cref="IUserRepository"/> that answers only <c>GetAccessStatusAsync</c>.</summary>
    public class AccessStatusOnly : DispatchProxy
    {
        private UserAccessStatus _status = null!;

        public static IUserRepository For(UserAccessStatus status)
        {
            var repository = Create<IUserRepository, AccessStatusOnly>();
            ((AccessStatusOnly)(object)repository)._status = status;
            return repository;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IUserRepository.GetAccessStatusAsync)
                ? Task.FromResult(_status)
                : throw new NotSupportedException(targetMethod?.Name);
    }
}
