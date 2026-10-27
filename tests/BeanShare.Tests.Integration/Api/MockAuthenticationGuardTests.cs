using BeanShare.Api.Infrastructure.Mocks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BeanShare.Tests.Integration.Api;

// The API starts with mock authentication only in Development or Testing, or when it is allowed explicitly.
// Pure unit tests; no database or container.
public sealed class MockAuthenticationGuardTests
{
    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Testing", null, true)]
    [InlineData("Production", null, false)]
    [InlineData("Staging", null, false)]
    [InlineData("Production", "false", false)]
    [InlineData("Production", "true", true)]
    public void MockAuthentication_IsAllowedOnlyWhereItIsSafe(string environment, string? allow, bool allowed)
    {
        var env = new Environment(environment);
        var config = Configuration(allow);

        MockAuthenticationGuard.IsAllowed(env, config).Should().Be(allowed);
        var ensure = () => MockAuthenticationGuard.EnsureAllowed(env, config);
        if (allowed)
            ensure.Should().NotThrow();
        else
            ensure.Should().Throw<InvalidOperationException>().WithMessage($"*{MockAuthenticationGuard.AllowSetting}*");
    }

    [Fact]
    public void TheApiStartup_ChecksTheGuardBeforeRegisteringMockAuthentication()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Presentation", "BeanShare.Api", "Program.cs"));

        var guard = program.IndexOf("MockAuthenticationGuard.EnsureAllowed(", StringComparison.Ordinal);
        var scheme = program.IndexOf("AddScheme<AuthenticationSchemeOptions, MockAuthenticationHandler>", StringComparison.Ordinal);

        guard.Should().BePositive();
        guard.Should().BeLessThan(scheme);
    }

    private static IConfiguration Configuration(string? allow) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(allow is null
                ? []
                : new Dictionary<string, string?> { [MockAuthenticationGuard.AllowSetting] = allow })
            .Build();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeanShare.slnf")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("BeanShare.slnf not found above the test output");
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "BeanShare.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
