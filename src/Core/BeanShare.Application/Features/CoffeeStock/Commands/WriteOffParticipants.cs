using BeanShare.Application.Abstractions;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

/// <summary>
/// Who may share a write-off: current members that are not merged placeholders, read uncached;
/// the equal-split fallback also leaves placeholders out.
/// </summary>
internal static class WriteOffParticipants
{
    public static async Task<List<ConsumptionEntry>> ShareableCupsAsync(
        Space space,
        IEnumerable<ConsumptionEntry> cups,
        IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var members = cups.Where(c => space.HasMember(c.UserId)).ToList();
        var merged = await userRepository.GetMergedMapAsync(members.Select(c => c.UserId).ToHashSet(), cancellationToken);
        return members.Where(c => !merged.ContainsKey(c.UserId)).ToList();
    }

    public static async Task<List<UserId>> EqualSplitMembersAsync(Space space, IUserRepository userRepository, CancellationToken cancellationToken)
    {
        var result = new List<UserId>();
        foreach (var member in space.Members)
        {
            var status = await userRepository.GetAccessStatusAsync(member.UserId, cancellationToken);
            if (status is { Exists: true, IsPlaceholder: false, IsMerged: false })
                result.Add(member.UserId);
        }

        return result;
    }
}
