namespace BeanShare.Api.Infrastructure.Mocks;

/// <summary>
/// Mock authentication trusts a request header for the user id, so the API only accepts it in Development
/// or Testing, or when <see cref="AllowSetting"/> is set explicitly (a local copy for a dry run).
/// </summary>
public static class MockAuthenticationGuard
{
    public const string AllowSetting = "AllowMockAuthentication";

    public static bool IsAllowed(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment()
        || environment.IsEnvironment("Testing")
        || configuration.GetValue<bool>(AllowSetting);

    /// <summary>Stops startup when mock authentication is configured where it is not allowed.</summary>
    public static void EnsureAllowed(IHostEnvironment environment, IConfiguration configuration)
    {
        if (!IsAllowed(environment, configuration))
        {
            throw new InvalidOperationException(
                $"UseMockAuthentication is set in the '{environment.EnvironmentName}' environment. Mock authentication is only " +
                $"allowed in Development or Testing, or with {AllowSetting}=true.");
        }
    }
}
