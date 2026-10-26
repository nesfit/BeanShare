using System.Security.Claims;
using BeanShare.Api.Endpoints.Users;
using BeanShare.Application.Common;
using BeanShare.Application.Services;
using FastEndpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeanShare.Tests.Integration.Api;

// CR5-1 / A5-4: the API link endpoint answers a failure like sync does (403 SYNC_FAILED), never a 500.
// Pure unit tests (FastEndpoints' Factory, a throwing sync service); no database or container.
[Collection(FastEndpointsFactoryCollection.Name)]
public sealed class PlaceholderLinkEndpointTests
{
    public PlaceholderLinkEndpointTests() => FastEndpointsFactoryIsolation.ResetResolver();

    [Fact]
    public async Task AFailingResolve_Is403SyncFailed()
    {
        var endpoint = Factory.Create<ResolvePlaceholderLinkEndpoint>(
            new ThrowingSync(), NullLogger<ResolvePlaceholderLinkEndpoint>.Instance);

        await endpoint.HandleAsync(new ResolvePlaceholderLinkRequest { Accept = true }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(403);
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.SyncFailed);
    }

    private sealed class ThrowingSync : IUserSynchronizationService
    {
        public Task<UserSyncOutcome> SyncFromClaimsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("unique index");

        public Task<LinkResolveOutcome> ResolvePlaceholderLinkAsync(ClaimsPrincipal principal, bool accept, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("unique index");
    }
}
