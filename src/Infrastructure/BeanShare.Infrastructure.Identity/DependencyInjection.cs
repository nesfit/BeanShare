using BeanShare.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace BeanShare.Infrastructure.Identity;

public static class DependencyInjection
{
    /// <summary>Registers <see cref="ICurrentPrincipalAccessor"/> and the sync settings; every host that registers an IUserContext calls it.</summary>
    public static IServiceCollection AddCurrentPrincipalAccessor(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentPrincipalAccessor, CurrentPrincipalAccessor>();
        if (configuration is not null)
        {
            services.AddSingleton(new BeanShare.Application.Services.UserSyncSettings
            {
                RequireVerifiedEmailForLinking = configuration.GetValue("Authentication:RequireVerifiedEmailForLinking", true)
            });
        }

        return services;
    }

    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, OidcUserContext>();
        services.AddCurrentPrincipalAccessor();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.LogoutPath = "/logout";
            options.AccessDeniedPath = "/access-denied";
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
        });

        // Add Google OAuth only if credentials are configured
        var googleClientId = configuration["Authentication:Google:ClientId"];
        var googleClientSecret = configuration["Authentication:Google:ClientSecret"];
        if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
        {
            authBuilder.AddGoogle(options =>
            {
                options.ClientId = googleClientId;
                options.ClientSecret = googleClientSecret;

                options.CallbackPath = "/signin-google";
                options.SaveTokens = true;

                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "sub");
                options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
                options.ClaimActions.MapJsonKey("picture", "picture");

                options.Scope.Add("profile");
                options.Scope.Add("email");
            });
        }

        // Add Facebook OAuth only if credentials are configured
        var facebookAppId = configuration["Authentication:Facebook:AppId"];
        var facebookAppSecret = configuration["Authentication:Facebook:AppSecret"];
        if (!string.IsNullOrEmpty(facebookAppId) && !string.IsNullOrEmpty(facebookAppSecret))
        {
            authBuilder.AddFacebook(options =>
            {
                options.AppId = facebookAppId;
                options.AppSecret = facebookAppSecret;

                options.CallbackPath = "/signin-facebook";
                options.SaveTokens = true;

                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
                options.ClaimActions.MapJsonKey("picture", "picture");

                options.Scope.Add("email");
                options.Scope.Add("public_profile");

                options.Fields.Add("name");
                options.Fields.Add("email");
                options.Fields.Add("picture");
            });
        }

        services.AddAuthorizationBuilder()
            .AddPolicy("RequireAuthenticatedUser", policy => policy.RequireAuthenticatedUser())
            .AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));

        return services;
    }
}