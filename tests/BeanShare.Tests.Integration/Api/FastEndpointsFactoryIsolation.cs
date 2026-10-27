using System.Reflection;
using FastEndpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BeanShare.Tests.Integration.Api;

/// <summary>
/// Pure endpoint tests built with FastEndpoints' <c>Factory.Create</c>. They run in this non-parallel collection,
/// alone, and reset FastEndpoints' process-wide resolver before each test.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FastEndpointsFactoryCollection : ICollectionFixture<StaleFastEndpointsResolverFixture>
{
    public const string Name = "FastEndpoints Factory";
}

/// <summary>
/// FastEndpoints keeps one process-wide service resolver (<c>FastEndpoints.Config._resolver</c>). Every API host a
/// <c>WebApplicationFactory</c> starts points it at that host's provider, and <c>Factory.Create</c> (and
/// <c>Factory.RegisterTestServices</c>) reuse it once set. After that host is disposed they throw
/// <see cref="ObjectDisposedException"/>, so a pure endpoint test fails whenever a host-based test class ran
/// earlier in the same process. Clearing the field makes <c>Factory.Create</c> build its own unit-test resolver,
/// as it does when nothing set one.
/// </summary>
public static class FastEndpointsFactoryIsolation
{
    private static readonly FieldInfo Resolver =
        typeof(Factory).Assembly.GetType("FastEndpoints.Config")?.GetField("_resolver", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "FastEndpoints.Config._resolver was not found; adapt FastEndpointsFactoryIsolation to this FastEndpoints version");

    public static void ResetResolver() => Resolver.SetValue(null, null);
}

/// <summary>
/// Starts an API host (in-memory database, mock authentication, no Docker) and disposes it before the collection's
/// first test, as an earlier host-based class does in a full run. Every test of the collection therefore meets a
/// resolver that points at a disposed provider, and passes only because it resets it.
/// </summary>
public sealed class StaleFastEndpointsResolverFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var host = new InMemoryApiHost();
        using var client = host.CreateClient();
        (await client.GetAsync("/api/me")).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class InMemoryApiHost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("UseMockAuthentication", "true");
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseEnvironment("Testing");
        }
    }
}
