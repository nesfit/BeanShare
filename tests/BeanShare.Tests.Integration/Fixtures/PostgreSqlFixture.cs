using BeanShare.Api.Infrastructure.Mocks;
using BeanShare.Application.Abstractions;
using BeanShare.Infrastructure.Identity;
using BeanShare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace BeanShare.Tests.Integration.Fixtures;

public sealed class PostgreSqlFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("beanshare_test")
        .WithUsername("beanshare_test")
        .WithPassword("beanshare_test123")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _postgresContainer.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("UseMockAuthentication", "true");
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<BeanShareDbContext>) ||
                           d.ServiceType == typeof(DbContextOptions) ||
                           d.ServiceType == typeof(BeanShareDbContext))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<BeanShareDbContext>(options =>
            {
                options.UseNpgsql(ConnectionString);
                options.EnableSensitiveDataLogging();
                options.LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information);
            });

            services.RemoveAll<IUserContext>();
            services.AddHttpContextAccessor();
            services.AddScoped<IUserContext, MockUserContext>();

            services.RemoveAll<IAuthenticationService>();
            services.RemoveAll<IJwtTokenService>();
            services.AddScoped<IAuthenticationService, MockIdentityAuthenticationService>();
            services.AddSingleton<IJwtTokenService, MockJwtTokenService>();
        });

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
        });
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BeanShareDbContext>();
        await context.Database.MigrateAsync();
        await FixtureUsers.SeedAsync(Services);
    }

    /// <summary>A user row for an id other than the two seeded ones (e.g. the E2E's people).</summary>
    public Task EnsureUserAsync(Guid id, string email) => FixtureUsers.EnsureUserAsync(Services, id, email);

    public new async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}