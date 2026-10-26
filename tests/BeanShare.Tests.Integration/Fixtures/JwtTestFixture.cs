using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BeanShare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace BeanShare.Tests.Integration.Fixtures;

public sealed class JwtTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("beanshare_jwt_test")
        .WithUsername("beanshare_test")
        .WithPassword("beanshare_test123")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _postgresContainer.GetConnectionString();

    // JWT configuration for tests
    private readonly string _jwtSecret = "test-secret-key-for-jwt-authentication-in-integration-tests-minimum-256-bits";
    private readonly string _jwtIssuer = "https://test-issuer.local";
    private readonly string _jwtAudience = "beanshare-api-test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<BeanShareDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<BeanShareDbContext>(options =>
            {
                options.UseNpgsql(ConnectionString);
            });

            var authDescriptors = services.Where(s => s.ServiceType.FullName?.Contains("Authentication") == true).ToList();
            foreach (var authDescriptor in authDescriptors)
            {
                services.Remove(authDescriptor);
            }

            services.AddAuthentication("Bearer")
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = _jwtIssuer,
                        ValidateAudience = true,
                        ValidAudience = _jwtAudience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret)),
                        ClockSkew = TimeSpan.FromMinutes(5)
                    };
                    options.RequireHttpsMetadata = false;
                });

            services.AddAuthorization();
        });

        builder.UseEnvironment("Testing");

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

    public new async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await base.DisposeAsync();
    }

    public string CreateTestJwtToken(string userId = "test-user-id", string email = "test@example.com", string[]? roles = null)
    {
        var claims = new List<Claim>
        {
            new("oid", userId),
            new(ClaimTypes.NameIdentifier, userId),
            new("preferred_username", email),
            new(ClaimTypes.Email, email),
            new("sub", userId)
        };

        roles ??= ["User"];
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtIssuer,
            audience: _jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public HttpClient CreateAuthenticatedClient(string userId = "test-user-id", string email = "test@example.com", string[]? roles = null)
    {
        var client = CreateClient();
        if (roles != null)
        {
            var token = CreateTestJwtToken(userId, email, roles);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }
}