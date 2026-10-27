namespace BeanShare.Domain.Enums;

public enum MembershipChangeType
{
    MemberJoined,
    MemberPromoted,
    MemberDemoted,
    MemberRemoved,

    /// <summary>An admin added a placeholder member; notifies nobody.</summary>
    PlaceholderAdded
}