using BeanShare.BlazorWeb.Components;
using BeanShare.BlazorWeb.Endpoints;
using BeanShare.Application;
using BeanShare.Infrastructure;
using BeanShare.Infrastructure.Identity;
using BeanShare.Infrastructure.Persistence;
using BeanShare.Infrastructure.Persistence.Seeds;
using BeanShare.Infrastructure.Services;
using BeanShare.SharedUi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=beanshare_dev;Username=beanshare;Password=beanshare123";

var useOidc = builder.Configuration.GetValue<bool>("UseOidc", false);
var useInMemoryDatabase = builder.Configuration.GetValue<bool>("UseInMemoryDatabase", false);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddApplication();
builder.Services.AddScopedSender();
builder.Services.AddInfrastructure(connectionString, useInMemoryDatabase: useInMemoryDatabase);
builder.Services.AddMemoryCache();
builder.Services.AddExchangeRates(builder.Configuration);
builder.Services.AddCommunicationServices(builder.Configuration);
builder.Services.AddHttpContextAccessor();

if (!useInMemoryDatabase)
{
    builder.Services.AddDataProtection()
        .PersistKeysToDbContext<BeanShareDbContext>();
}

if (useOidc)
{
    // OIDC configuration
    var oidcAuthority = builder.Configuration["Oidc:Authority"]
        ?? throw new InvalidOperationException("Oidc:Authority not configured");
    var oidcClientId = builder.Configuration["Oidc:ClientId"] ?? "beanshare-web";
    var oidcClientSecret = builder.Configuration["Oidc:ClientSecret"]
        ?? throw new InvalidOperationException("Oidc:ClientSecret not configured");
    var oidcIdentityProviderHintParam = builder.Configuration["Oidc:IdentityProviderHintParam"];
    var oidcResponseMode = builder.Configuration["Oidc:ResponseMode"];
    var oidcUseSecureCallbackCookies =
        builder.Configuration.GetValue<bool?>("Oidc:UseSecureCallbackCookies")
        ?? builder.Environment.IsProduction();

    // form_post delivers the callback via a cross-site POST, so its correlation/nonce
    // cookies must be SameSite=None — and browsers only accept None cookies that are
    // also Secure. Over plain http (local dev) that combination can never work: the
    // cookie is dropped and every login dies with "Correlation failed". Default to the
    // query response mode there; an explicit Oidc:ResponseMode still wins.
    if (string.IsNullOrWhiteSpace(oidcResponseMode) && !oidcUseSecureCallbackCookies)
    {
        oidcResponseMode = "query";
    }

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        // ExpireTimeSpan only bounds the ticket inside the cookie; MaxAge is what actually
        // tells the browser to keep it on disk. Without it the cookie is session-scoped and
        // iOS re-prompts for login every time it recycles the web view.
        options.Cookie.MaxAge = TimeSpan.FromDays(30);
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = oidcAuthority;
        options.ClientId = oidcClientId;
        options.ClientSecret = oidcClientSecret;
        options.ResponseType = "code";
        if (!string.IsNullOrWhiteSpace(oidcResponseMode))
        {
            options.ResponseMode = oidcResponseMode;
        }
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.RequireHttpsMetadata = builder.Environment.IsProduction();
        options.MapInboundClaims = false;
        options.CorrelationCookie.SecurePolicy = oidcUseSecureCallbackCookies
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.NonceCookie.SecurePolicy = oidcUseSecureCallbackCookies
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;

        var useFormPostResponseMode = string.IsNullOrWhiteSpace(options.ResponseMode)
            || string.Equals(options.ResponseMode, "form_post", StringComparison.OrdinalIgnoreCase);
        var callbackCookieSameSite = useFormPostResponseMode
            ? SameSiteMode.None
            : SameSiteMode.Lax;
        options.CorrelationCookie.SameSite = callbackCookieSameSite;
        options.NonceCookie.SameSite = callbackCookieSameSite;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        // Only map the claims we actually need from the UserInfo endpoint response.
        // This prevents Authentik group lists, extra profile fields, and other bulk
        // claims from being written into the auth cookie.
        options.ClaimActions.Clear();
        options.ClaimActions.MapUniqueJsonKey("sub", "sub");
        options.ClaimActions.MapUniqueJsonKey("email", "email");
        options.ClaimActions.MapUniqueJsonKey("preferred_username", "preferred_username");
        options.ClaimActions.MapUniqueJsonKey("name", "name");
        options.ClaimActions.MapUniqueJsonKey("identity_provider", "identity_provider");
        // Map groups/roles from UserInfo so that role-based authorization still works
        // for providers that only include these claims in the UserInfo response.
        options.ClaimActions.MapJsonKey(ClaimTypes.Role, "roles");
        options.ClaimActions.MapJsonKey(ClaimTypes.Role, "groups");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username",
            RoleClaimType = ClaimTypes.Role
        };

        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                if (!string.IsNullOrWhiteSpace(oidcIdentityProviderHintParam) &&
                    context.Properties.Items.TryGetValue(oidcIdentityProviderHintParam, out var hintValue) &&
                    !string.IsNullOrWhiteSpace(hintValue))
                {
                    context.ProtocolMessage.SetParameter(oidcIdentityProviderHintParam, hintValue);
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                // Extract roles from multiple OIDC claim formats for provider compatibility
                if (context.Principal != null)
                {
                    var identity = context.Principal.Identity as ClaimsIdentity;
                    if (identity != null)
                    {
                        // Keycloak: realm_access JSON with nested roles array
                        var realmAccessClaim = identity.FindFirst("realm_access");
                        if (realmAccessClaim != null)
                        {
                            try
                            {
                                var realmAccess = System.Text.Json.JsonDocument.Parse(realmAccessClaim.Value);
                                if (realmAccess.RootElement.TryGetProperty("roles", out var rolesElement))
                                {
                                    foreach (var role in rolesElement.EnumerateArray())
                                    {
                                        AddRoleClaims(identity, role.GetString());
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                                logger.LogError(ex, "Failed to parse realm_access claim");
                            }
                        }

                        var rolesClaim = identity.FindFirst("roles");
                        if (rolesClaim != null)
                        {
                            AddRoleClaims(identity, rolesClaim.Value);
                        }

                        foreach (var groupsClaim in identity.FindAll("groups").ToList())
                        {
                            AddRoleClaims(identity, groupsClaim.Value);
                        }
                    }
                }

                var syncService = context.HttpContext.RequestServices
                    .GetService<BeanShare.Application.Services.IUserSynchronizationService>();
                if (syncService != null && context.Principal != null)
                {
                    try
                    {
                        await syncService.SyncFromClaimsAsync(context.Principal);
                    }
                    catch (Exception ex)
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogError(ex, "Failed to sync user from OIDC claims");
                    }
                }

                // Strip non-essential claims from the ID token before the auth ticket is
                // written to the cookie. Keeping only what the application actually reads
                // prevents the cookie from chunking into multiple browser cookies (which
                // causes "400 Request Header Or Cookie Too Large" behind nginx).
                //
                // Removed claim types and why they are safe to remove:
                //   iss, aud, exp, iat, nbf, jti  – JWT validation fields; already consumed by the handler
                //   nonce, auth_time, acr, amr, sid, at_hash, c_hash, azp, session_state
                //                                  – OIDC protocol fields; not used after validation
                //   realm_access                   – Keycloak JSON blob; roles already expanded above
                //   roles, groups                  – raw claim strings; already expanded into ClaimTypes.Role
                //   given_name, family_name, locale, zoneinfo, updated_at, picture,
                //   website, phone_number, email_verified
                //                                  – extended profile fields not read by this application
                if (context.Principal?.Identity is ClaimsIdentity idToClean)
                {
                    var essentialClaimTypes = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "sub",
                        ClaimTypes.NameIdentifier,
                        "email",
                        "preferred_username",
                        "name",
                        "identity_provider",
                        ClaimTypes.Role,
                    };

                    foreach (var claim in idToClean.Claims
                        .Where(c => !essentialClaimTypes.Contains(c.Type))
                        .ToList())
                    {
                        idToClean.RemoveClaim(claim);
                    }
                }
            }
        };
    });

    builder.Services.AddScoped<BeanShare.Application.Abstractions.IUserContext, OidcUserContext>();
    builder.Services.AddScoped<BeanShare.Application.Services.IUserSynchronizationService, BeanShare.Application.Services.UserSynchronizationService>();
    builder.Services.AddScoped<Microsoft.AspNetCore.Authentication.IClaimsTransformation, BeanShare.Infrastructure.Identity.OidcClaimsTransformation>();
}
else
{
    builder.Services.AddIdentityInfrastructure(builder.Configuration);
    builder.Services.AddScoped<BeanShare.Application.Abstractions.IUserContext, BeanShare.BlazorWeb.Services.HttpUserContext>();
}

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<IThemeService, ThemeService>();
builder.Services.AddScoped<BeanShare.SharedUi.Services.IQrCodeService>(sp =>
{
    var qrCodeBaseUrl = ResolvePublicBaseUrl(
        sp,
        builder.Configuration.GetValue<string>("QrCodeBaseUrl"),
        "http://localhost:5126");

    return new BeanShare.SharedUi.Services.QrCodeService(qrCodeBaseUrl);
});
builder.Services.AddSingleton<BeanShare.SharedUi.Services.IQrScannerService, BeanShare.SharedUi.Services.BrowserQrScannerService>();
builder.Services.AddSingleton<BeanShare.SharedUi.Services.QrCodeResolveCache>();

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedProto
};
#pragma warning disable ASPDEPR005
forwardedHeadersOptions.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");

app.UseAuthentication();
app.UseAuthorization();

var includeWebDemoData = app.Environment.IsDevelopment()
    || app.Configuration.GetValue<bool>("IncludeDemoData");
await app.Services.InitializeBeanShareDatabaseAsync(includeWebDemoData);

app.UseAntiforgery();

using var avatarScope = app.Services.CreateScope();
var avatarStorage = avatarScope.ServiceProvider.GetRequiredService<AvatarStorageService>();
var uploadsRootPath = avatarStorage.GetUploadsRootPath();
Directory.CreateDirectory(uploadsRootPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsRootPath),
    RequestPath = "/uploads"
});
app.UseStaticFiles();
app.MapStaticAssets();

app.MapAccountEndpoints();
app.MapSettlementExportEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string ResolvePublicBaseUrl(IServiceProvider services, string? configuredValue, string developmentFallback)
{
    if (!string.IsNullOrWhiteSpace(configuredValue))
    {
        return configuredValue.TrimEnd('/');
    }

    var navigationManager = services.GetService<NavigationManager>();
    if (navigationManager != null && !string.IsNullOrWhiteSpace(navigationManager.BaseUri))
    {
        return navigationManager.BaseUri.TrimEnd('/');
    }

    var httpContext = services.GetRequiredService<IHttpContextAccessor>().HttpContext;
    if (httpContext != null)
    {
        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}".TrimEnd('/');
    }

    return developmentFallback.TrimEnd('/');
}

static void AddRoleClaims(ClaimsIdentity identity, string? rawValue)
{
    foreach (var roleValue in ExpandMultiValueClaim(rawValue))
    {
        if (!identity.HasClaim(ClaimTypes.Role, roleValue))
        {
            identity.AddClaim(new Claim(
                ClaimTypes.Role,
                roleValue));
        }
    }
}

static IEnumerable<string> ExpandMultiValueClaim(string? rawValue)
{
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        yield break;
    }

    var trimmedValue = rawValue.Trim();

    if (trimmedValue.StartsWith("["))
    {
        var parsedValues = TryParseJsonArrayClaim(trimmedValue);
        if (parsedValues != null)
        {
            foreach (var value in parsedValues)
            {
                yield return value;
            }

            yield break;
        }
    }

    yield return trimmedValue;
}

static List<string>? TryParseJsonArrayClaim(string rawValue)
{
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(rawValue);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }
    catch (System.Text.Json.JsonException)
    {
        return null;
    }
}
