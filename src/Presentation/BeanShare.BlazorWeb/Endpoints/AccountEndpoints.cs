using BeanShare.Domain.Enums;
using BeanShare.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace BeanShare.BlazorWeb.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/account");

        group.MapGet("/login", Login);
        group.MapPost("/external-login", ExternalLogin).DisableAntiforgery();
        group.MapGet("/external-callback", ExternalCallback);
        group.MapPost("/logout", Logout).DisableAntiforgery();
        // Antiforgery stays on: the link page posts its token.
        group.MapPost("/link/resolve", ResolveLink);

        return endpoints;
    }

    /// <summary>
    /// The person's answer to a pending placeholder link. On success the cookie is dropped and a silent
    /// SSO round trip rebuilds it for the claimed or the new account; a lost race says "changed meanwhile".
    /// </summary>
    private static async Task<IResult> ResolveLink(
        HttpContext httpContext,
        [FromForm] bool accept,
        [FromServices] BeanShare.Application.Services.IUserSynchronizationService syncService)
    {
        if (!BeanShare.Application.Services.OidcClaims.HasLinkPending(httpContext.User))
            return Results.Redirect("/");

        var outcome = await syncService.ResolvePlaceholderLinkAsync(httpContext.User, accept, httpContext.RequestAborted);
        if (outcome is not (BeanShare.Application.Services.LinkResolveOutcome.Claimed or BeanShare.Application.Services.LinkResolveOutcome.Declined))
            return Results.Redirect("/account/link?status=changed");

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/", IsPersistent = true },
            new[] { OpenIdConnectDefaults.AuthenticationScheme });
    }

    private static Task<IResult> Login(
        HttpContext httpContext,
        [FromQuery] string? returnUrl,
        [FromServices] IConfiguration configuration)
    {
        var useOidc = configuration.GetValue<bool>("UseOidc", false);

        if (useOidc)
        {
            // IsPersistent makes the cookie handler write Expires/Max-Age into the browser
            // cookie. Without it the auth cookie is a session cookie, which iOS drops every
            // time it evicts the Safari tab or the home-screen web app from memory.
            var properties = new AuthenticationProperties
            {
                RedirectUri = returnUrl ?? "/",
                IsPersistent = true
            };

            return Task.FromResult(Results.Challenge(properties, new[] { OpenIdConnectDefaults.AuthenticationScheme }));
        }

        return Task.FromResult(Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}"));
    }

    private static Task<IResult> ExternalLogin(
        [FromForm] string provider,
        [FromForm] string? returnUrl,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext)
    {
        var useOidc = configuration.GetValue<bool>("UseOidc", false);

        if (useOidc)
        {
            var properties = new AuthenticationProperties
            {
                RedirectUri = returnUrl ?? "/",
                IsPersistent = true
            };

            var idpHint = provider.ToLowerInvariant() switch
            {
                "google" => "google",
                "facebook" => "facebook",
                _ => null
            };

            if (idpHint != null)
            {
                var hintParam = configuration.GetValue<string>("Oidc:IdentityProviderHintParam") ?? "kc_idp_hint";
                properties.Items[hintParam] = idpHint;
            }

            return Task.FromResult(Results.Challenge(properties, new[] { OpenIdConnectDefaults.AuthenticationScheme }));
        }

        if (string.IsNullOrEmpty(provider) ||
            (provider != "Google" && provider != "Facebook"))
        {
            return Task.FromResult(Results.BadRequest("Invalid provider"));
        }

        var redirectUrl = $"/account/external-callback?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}";

        var authProperties = new AuthenticationProperties
        {
            RedirectUri = redirectUrl
        };

        return Task.FromResult(Results.Challenge(authProperties, new[] { provider }));
    }

    private static async Task<IResult> ExternalCallback(
        HttpContext httpContext,
        [FromQuery] string? returnUrl,
        [FromServices] IConfiguration configuration,
        [FromServices] IServiceProvider serviceProvider,
        [FromServices] ILogger logger)
    {
        var useOidc = configuration.GetValue<bool>("UseOidc", false);

        if (useOidc)
        {
            var finalReturnUrl = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
            return Results.Redirect(finalReturnUrl);
        }

        var authService = serviceProvider.GetService<Infrastructure.Identity.IAuthenticationService>();
        if (authService == null)
        {
            return Results.Redirect("/login?error=service_unavailable");
        }

        try
        {
            var result = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (result?.Succeeded != true)
            {
                var externalResult = await httpContext.AuthenticateAsync();

                if (externalResult?.Succeeded != true || externalResult.Principal == null)
                {
                    return Results.Redirect("/login?error=authentication_failed");
                }

                var providerName = externalResult.Ticket?.AuthenticationScheme ?? "Unknown";

                if (!Enum.TryParse<AuthenticationProvider>(providerName, true, out var provider))
                {
                    return Results.Redirect("/login?error=unsupported_provider");
                }

                var user = await authService.GetOrCreateUserAsync(externalResult.Principal, provider);

                await authService.SignInAsync(httpContext, user);
            }

            var finalReturnUrl = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
            return Results.Redirect(finalReturnUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "External callback error during authentication");
            return Results.Redirect("/login?error=callback_failed");
        }
    }

    private static async Task<IResult> Logout(
        HttpContext httpContext,
        [FromServices] IConfiguration configuration,
        [FromServices] IServiceProvider serviceProvider)
    {
        var useOidc = configuration.GetValue<bool>("UseOidc", false);

        if (useOidc)
        {
            var properties = new AuthenticationProperties
            {
                RedirectUri = "/"
            };

            return Results.SignOut(properties, new[] { CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme });
        }

        var authService = serviceProvider.GetService<Infrastructure.Identity.IAuthenticationService>();
        if (authService != null)
        {
            await authService.SignOutAsync(httpContext);
        }
        else
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return Results.Redirect("/login");
    }
}
