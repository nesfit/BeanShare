using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;

namespace BeanShare.Application.Features.Spaces.Services;

/// <summary>
/// Creates placeholder members (D21, DS-14) for the add dialog and for the paste previews, inside the
/// caller's unit of work: <see cref="IUserRepository.AddAsync"/> does not save, so a racing duplicate email
/// surfaces through the UoW translation as EMAIL_IN_USE and a paste stays all or nothing (U10).
/// </summary>
public static class PlaceholderMembers
{
    public const int MaxNameLength = 100;

    public static async Task<Result<User>> AddAsync(
        Space space,
        string name,
        string? email,
        IUserRepository userRepository,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (ValidateName(name) is { } nameError)
            return Result<User>.Failure(nameError);

        if (!string.IsNullOrWhiteSpace(email))
        {
            if (ValidateEmail(email) is { } emailError)
                return Result<User>.Failure(emailError);
            if (await userRepository.FindByEmailAsync(email, cancellationToken) is not null)
                return Result<User>.Failure(Error.EmailInUse());
        }

        var placeholder = User.CreatePlaceholder(name, email, clock.UtcNow);
        await userRepository.AddAsync(placeholder, cancellationToken);
        space.AddPlaceholderMember(placeholder.Id, clock);
        return Result<User>.Success(placeholder);
    }

    public static Error? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.ValidationFailure("Name", "A name is required");
        if (name.Trim().Length > MaxNameLength)
            return Error.ValidationFailure("Name", $"The name must be at most {MaxNameLength} characters");
        return null;
    }

    public static Error? ValidateEmail(string email)
    {
        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1 || trimmed.Length > 254 || trimmed.Any(char.IsWhiteSpace))
            return Error.ValidationFailure("Email", $"'{trimmed}' is not an email address");
        if (User.IsSentinelEmail(trimmed))
            return Error.ValidationFailure("Email", "This address is reserved");
        return null;
    }

    /// <summary>A placeholder for a pasted member reference: an email keeps the address, a name has none.</summary>
    public static (string Name, string? Email) FromPastedText(string text)
    {
        var value = text.Trim();
        if (!value.Contains('@'))
            return (value, null);

        var local = value[..value.IndexOf('@')];
        return (string.IsNullOrWhiteSpace(local) ? value : local, value);
    }
}
