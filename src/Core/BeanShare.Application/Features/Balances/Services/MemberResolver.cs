using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;

namespace BeanShare.Application.Features.Balances.Services;

/// <summary>
/// Resolves a pasted member reference — an email (case-insensitive) or an exact display name
/// (case-insensitive) — to a current member of one space.
/// </summary>
public sealed class MemberResolver
{
    private readonly IReadOnlyList<(UserId Id, string Name, string Email)> _members;

    private MemberResolver(IReadOnlyList<(UserId Id, string Name, string Email)> members)
    {
        _members = members;
    }

    public static async Task<MemberResolver> CreateAsync(Space space, IUserService userService, CancellationToken cancellationToken)
    {
        var users = await userService.GetByIdsAsync(space.Members.Select(m => m.UserId), cancellationToken);
        var members = users
            .Where(u => space.HasMember(u.Id))
            .Select(u => (u.Id, u.Name, u.Email))
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
                : MemberMatch.Failed($"'{value}' is not a member of this space");
        }

        var byName = _members.Where(m => string.Equals(m.Name.Trim(), value, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count switch
        {
            1 => MemberMatch.Found(byName[0].Id, byName[0].Name),
            0 => MemberMatch.Failed($"'{value}' is not a member of this space"),
            _ => MemberMatch.Failed($"More than one member is called '{value}'; use the email instead")
        };
    }
}

public sealed record MemberMatch(UserId? UserId, string? Name, string? Error)
{
    public static MemberMatch Found(UserId userId, string name) => new(userId, name, null);
    public static MemberMatch Failed(string error) => new(null, null, error);
}
