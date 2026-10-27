using BeanShare.Application.Abstractions;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;

namespace BeanShare.Application.Features.Balances.Services;

/// <summary>
/// Resolves a pasted member reference — an email (case-insensitive) or an exact display name
/// (case-insensitive) — to a current member of one space. Merged placeholders are hidden.
/// </summary>
public sealed class MemberResolver
{
    private readonly IReadOnlyList<(UserId Id, string Name, string Email)> _members;

    private MemberResolver(IReadOnlyList<(UserId Id, string Name, string Email)> members)
    {
        _members = members;
    }

    public static async Task<MemberResolver> CreateAsync(
        Space space,
        IUserService userService,
        IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var memberIds = space.Members.Select(m => m.UserId).ToList();
        var users = await userService.GetByIdsAsync(memberIds, cancellationToken);
        var merged = await userRepository.GetMergedMapAsync(memberIds, cancellationToken);
        var members = users
            .Where(u => space.HasMember(u.Id) && !merged.ContainsKey(u.Id))
            .Select(u => (u.Id, u.Name, u.PublicEmail))
            .ToList();
        return new MemberResolver(members);
    }

    public bool IsMember(UserId userId) => _members.Any(m => m.Id == userId);

    public string NameOf(UserId userId) =>
        _members.FirstOrDefault(m => m.Id == userId).Name ?? "Unknown";

    public MemberMatch Resolve(string text)
    {
        var value = text.Trim();
        if (value.Length == 0)
            return MemberMatch.Failed("The member is missing");

        if (value.Contains('@'))
        {
            var byEmail = _members.Where(m => string.Equals(m.Email, value, StringComparison.OrdinalIgnoreCase)).ToList();
            return byEmail.Count == 1
                ? MemberMatch.Found(byEmail[0].Id, byEmail[0].Name)
                : MemberMatch.Unknown(value);
        }

        var byName = _members.Where(m => string.Equals(m.Name.Trim(), value, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count switch
        {
            1 => MemberMatch.Found(byName[0].Id, byName[0].Name),
            0 => MemberMatch.Unknown(value),
            _ => MemberMatch.Failed($"More than one member is called '{value}'; use the email instead")
        };
    }
}

/// <param name="IsUnknown">Nobody matches: the paste can offer to create a placeholder instead.</param>
public sealed record MemberMatch(UserId? UserId, string? Name, string? Error, bool IsUnknown = false)
{
    public static MemberMatch Found(UserId userId, string name) => new(userId, name, null);
    public static MemberMatch Failed(string error) => new(null, null, error);
    public static MemberMatch Unknown(string text) => new(null, null, $"'{text}' is not a member of this space", IsUnknown: true);
}
