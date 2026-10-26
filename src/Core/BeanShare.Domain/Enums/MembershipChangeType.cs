namespace BeanShare.Domain.Enums;

public enum MembershipChangeType
{
    MemberJoined,
    MemberPromoted,
    MemberDemoted,
    MemberRemoved,

    /// <summary>An admin added a placeholder member (D21); notifies nobody (F11).</summary>
    PlaceholderAdded
}