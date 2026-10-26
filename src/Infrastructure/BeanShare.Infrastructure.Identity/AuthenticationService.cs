using System.Security.Claims;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BeanShare.Infrastructure.Identity;

public interface IAuthenticationService
{
    Task<User> GetOrCreateUserAsync(ClaimsPrincipal externalPrincipal, AuthenticationProvider provider);
    Task<(bool Success, User? User, string? ErrorMessage)> RegisterAsync(string email, string name, string password);
    Task<(bool Success, User? User, string? ErrorMessage)> LoginAsync(string email, string password);
    Task SignInAsync(HttpContext httpContext, User user, AuthenticationProperties? properties = null);
    Task SignOutAsync(HttpContext httpContext);
}

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly BeanShareDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IClock _clock;

    public AuthenticationService(BeanShareDbContext dbContext, IPasswordHasher passwordHasher, IClock clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _clock = clock;
    }

    public async Task<User> GetOrCreateUserAsync(ClaimsPrincipal externalPrincipal, AuthenticationProvider provider)
    {
        var providerUserId = externalPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? externalPrincipal.FindFirstValue("sub")
                          ?? throw new InvalidOperationException("Provider user ID not found in claims");

        var email = externalPrincipal.FindFirstValue(ClaimTypes.Email)
                  ?? externalPrincipal.FindFirstValue("email")
                  ?? throw new InvalidOperationException("Email not found in claims");

        var name = externalPrincipal.FindFirstValue(ClaimTypes.Name)
                ?? externalPrincipal.FindFirstValue("name")
                ?? email.Split('@')[0];

        var pictureUrl = externalPrincipal.FindFirstValue("picture");

        var existingUser = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Provider == provider && u.ProviderUserId == providerUserId);

        if (existingUser != null)
        {
            existingUser.UpdateLastLogin(_clock.UtcNow);
            existingUser.UpdateProfile(name, pictureUrl);
            await _dbContext.SaveChangesAsync();
            return existingUser;
        }

        var newUser = User.CreateWithProvider(email, name, provider, providerUserId, _clock.UtcNow, pictureUrl);
        _dbContext.Users.Add(newUser);
        await _dbContext.SaveChangesAsync();

        return newUser;
    }

    public async Task<(bool Success, User? User, string? ErrorMessage)> RegisterAsync(string email, string name, string password)
    {
        if (string.IsNullOrWhiteSpace(email))
            return (false, null, "Email is required");

        if (string.IsNullOrWhiteSpace(name))
            return (false, null, "Name is required");

        if (string.IsNullOrWhiteSpace(password))
            return (false, null, "Password is required");

        if (password.Length < 8)
            return (false, null, "Password must be at least 8 characters");

        // Emails compare case-insensitively (B20).
        var emailLower = email.Trim().ToLowerInvariant();
        var existingUser = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == emailLower);

        if (existingUser is { IsPlaceholder: true })
            return (false, null, "An admin created a placeholder for this email; sign in with SSO to claim it");

        if (existingUser != null)
            return (false, null, "An account with this email already exists");

        var passwordHash = _passwordHasher.HashPassword(password);

        var user = User.CreateWithPassword(email, name, passwordHash, _clock.UtcNow);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return (true, user, null);
    }

    public async Task<(bool Success, User? User, string? ErrorMessage)> LoginAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email))
            return (false, null, "Email is required");

        if (string.IsNullOrWhiteSpace(password))
            return (false, null, "Password is required");

        // Any account with a password, whatever provider a verified SSO match linked since (B24); emails
        // compare case-insensitively (B20).
        var emailLower = email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == emailLower && u.PasswordHash != null);

        if (user == null)
            return (false, null, "Invalid email or password");

        if (user.PasswordHash == null || !_passwordHasher.VerifyPassword(password, user.PasswordHash))
            return (false, null, "Invalid email or password");

        user.UpdateLastLogin(_clock.UtcNow);
        await _dbContext.SaveChangesAsync();

        return (true, user, null);
    }

    public async Task SignInAsync(HttpContext httpContext, User user, AuthenticationProperties? properties = null)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.Value.ToString()),
            new Claim("sub", user.Id.Value.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("email", user.Email),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim("name", user.Name),
            new Claim("provider", user.Provider.ToString()),
        };

        if (!string.IsNullOrEmpty(user.PictureUrl))
        {
            claims.Add(new Claim("picture", user.PictureUrl));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        properties ??= new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
        };

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);
    }

    public async Task SignOutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
