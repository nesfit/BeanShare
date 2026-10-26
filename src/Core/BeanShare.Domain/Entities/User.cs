using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Domain.Entities;

public sealed class User
{
    public UserId Id { get; private set; }
    public string Email { get; private set; }
    public string Name { get; private set; }
    public string? PictureUrl { get; private set; }
    public AuthenticationProvider Provider { get; private set; }
    public string? ProviderUserId { get; private set; }
    public string? PasswordHash { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime LastLoginAt { get; private set; }
    public string? PreferredCurrencyCode { get; private set; }
    /// <summary>Revolut username/handle (bare, without the revolut.me/ prefix) for settlement payment links.</summary>
    public string? RevolutTag { get; private set; }
    public SystemRole SystemRole { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? DeactivatedAt { get; private set; }

    /// <summary>A member an admin created for someone without an account (D21); cannot log in.</summary>
    public bool IsPlaceholder { get; private set; }

    /// <summary>Set when an admin merged this placeholder into a member (DS-32); its money is swept there.</summary>
    public UserId? MergedIntoUserId { get; private set; }

    private User()
    {
        Email = string.Empty;
        Name = string.Empty;
        SystemRole = SystemRole.User;
        IsActive = true;
    }

    private User(UserId id, string email, string name, AuthenticationProvider provider, DateTime createdAt, string? providerUserId = null, string? passwordHash = null, string? pictureUrl = null)
    {
        Id = id;
        Email = email;
        Name = name;
        Provider = provider;
        ProviderUserId = providerUserId;
        PasswordHash = passwordHash;
        PictureUrl = pictureUrl;
        CreatedAt = createdAt;
        LastLoginAt = createdAt;
        SystemRole = SystemRole.User;
        IsActive = true;
    }

    public static User CreateWithPassword(string email, string name, string passwordHash, DateTime createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new User(new UserId(Guid.NewGuid()), email, name, AuthenticationProvider.Email, createdAt, null, passwordHash, null);
    }

    public static User CreateWithIdAndPassword(Guid id, string email, string name, string passwordHash, DateTime createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new User(new UserId(id), email, name, AuthenticationProvider.Email, createdAt, null, passwordHash, null);
    }

    public static User CreateWithProvider(string email, string name, AuthenticationProvider provider, string providerUserId, DateTime createdAt, string? pictureUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerUserId);

        return new User(new UserId(Guid.NewGuid()), email, name, provider, createdAt, providerUserId, null, pictureUrl);
    }

    public static User CreateFromOidc(Guid oidcUserId, string email, string name, AuthenticationProvider provider, DateTime createdAt, string? pictureUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new User(new UserId(oidcUserId), email, name, provider, createdAt, oidcUserId.ToString(), null, pictureUrl);
    }

    public void UpdateLastLogin(DateTime lastLoginAt)
    {
        LastLoginAt = lastLoginAt;
    }

    public void UpdateProfile(string name, string? pictureUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        PictureUrl = pictureUrl;
    }

    public void UpdatePassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    /// <summary>
    /// Set the Revolut handle used for "pay" links on settlements. Accepts a full link
    /// (revolut.me/handle), an "@handle", or a bare handle — only the handle is stored.
    /// </summary>
    public void SetRevolutTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            RevolutTag = null;
            return;
        }

        var t = tag.Trim();
        var slash = t.LastIndexOf('/');
        if (slash >= 0)
        {
            t = t[(slash + 1)..];
        }
        t = t.TrimStart('@').Trim();
        if (t.Length > 30)
        {
            t = t[..30];
        }

        RevolutTag = string.IsNullOrWhiteSpace(t) ? null : t;
    }

    public void SetPreferredCurrency(string? currencyCode)
    {
        if (currencyCode is not null)
        {
            _ = Currency.Create(currencyCode);
        }
        PreferredCurrencyCode = currencyCode?.ToUpperInvariant();
    }

    public void PromoteToSystemAdmin()
    {
        SystemRole = SystemRole.SystemAdmin;
    }

    public void DemoteToUser()
    {
        SystemRole = SystemRole.User;
    }

    public void SetSystemRole(SystemRole role)
    {
        SystemRole = role;
    }

    public void Deactivate(DateTime deactivatedAt)
    {
        if (!IsActive)
            return;

        IsActive = false;
        DeactivatedAt = deactivatedAt;
    }

    public void Reactivate()
    {
        if (IsActive)
            return;

        IsActive = true;
        DeactivatedAt = null;
    }

    public bool IsSystemAdmin => SystemRole == SystemRole.SystemAdmin;

    /// <summary>Domain of the sentinel address a placeholder without an email gets (RFC 6761 `.invalid`, DS-14).</summary>
    public const string SentinelEmailDomain = "placeholder.invalid";

    public static string SentinelEmailFor(UserId id) => $"placeholder-{id.Value:N}@{SentinelEmailDomain}";

    public static bool IsSentinelEmail(string? email) =>
        email is not null && email.EndsWith("@" + SentinelEmailDomain, StringComparison.OrdinalIgnoreCase);

    /// <summary>False for a sentinel address: nothing can be delivered to it.</summary>
    public bool HasDeliverableEmail => !IsSentinelEmail(Email);

    /// <summary>The email to show or mail; "" for a sentinel (DS-33).</summary>
    public string PublicEmail => HasDeliverableEmail ? Email : string.Empty;

    public bool IsMerged => MergedIntoUserId.HasValue;

    /// <summary>A member without an account; without an email it gets the sentinel address (DS-14).</summary>
    public static User CreatePlaceholder(string name, string? email, DateTime createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var id = new UserId(Guid.NewGuid());
        var address = string.IsNullOrWhiteSpace(email) ? SentinelEmailFor(id) : NormalizeEmail(email);
        return new User(id, address, name.Trim(), AuthenticationProvider.Email, createdAt)
        {
            IsPlaceholder = true
        };
    }

    public void UpdatePlaceholder(string name, string? email)
    {
        EnsurePlaceholder();
        if (IsMerged)
            throw new InvalidOperationException("A merged placeholder cannot be edited");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? SentinelEmailFor(Id) : NormalizeEmail(email);
    }

    /// <summary>The person accepted the link (DS-31): same id, now a normal user with the provider link.</summary>
    public void ClaimPlaceholder(AuthenticationProvider provider, string providerUserId, DateTime now)
    {
        EnsurePlaceholder();
        if (IsMerged)
            throw new InvalidOperationException("A merged placeholder cannot be claimed");
        ArgumentException.ThrowIfNullOrWhiteSpace(providerUserId);

        IsPlaceholder = false;
        Provider = provider;
        ProviderUserId = providerUserId;
        LastLoginAt = now;
    }

    /// <summary>Stores the provider link of a verified email match (D24, DS-29).</summary>
    public void LinkProvider(AuthenticationProvider provider, string providerUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerUserId);
        Provider = provider;
        ProviderUserId = providerUserId;
    }

    /// <summary>Gives up the real address (decline or merge), so it can belong to a real account.</summary>
    public void ReleaseEmail()
    {
        EnsurePlaceholder();
        Email = SentinelEmailFor(Id);
    }

    public void MarkMergedInto(UserId target)
    {
        EnsurePlaceholder();
        if (target == Id)
            throw new InvalidOperationException("A placeholder cannot be merged into itself");
        if (IsMerged)
            throw new InvalidOperationException("The placeholder is already merged");

        MergedIntoUserId = target;
    }

    private void EnsurePlaceholder()
    {
        if (!IsPlaceholder)
            throw new InvalidOperationException("The user is not a placeholder");
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
