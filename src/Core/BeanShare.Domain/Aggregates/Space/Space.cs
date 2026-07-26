using BeanShare.Domain.Common;
using BeanShare.Domain.Events;
using BeanShare.Domain.ValueObjects;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Exceptions;

namespace BeanShare.Domain.Aggregates.Space;

public sealed class Space : AggregateRoot
{
    private readonly List<SpaceMembership> _members = [];

    public SpaceId Id { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public Currency Currency { get; private set; } = default!;
    public InviteCode InviteCode { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Restock-planner tuning (per space). Defaults match a ~1-week delivery lead time
    // and a worthwhile batch-order threshold; admins can override both.
    public int RestockLeadTimeDays { get; private set; } = 7;
    public decimal RestockMinOrderValue { get; private set; } = 2000m;

    public IReadOnlyCollection<SpaceMembership> Members => _members.AsReadOnly();

    private Space()
    {
    }
    
    internal Space(SpaceId id, string name, Currency currency, InviteCode inviteCode, UserId creatorUserId, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new SpaceDomainException("Space name cannot be empty");

        Id = id;
        Name = name.Trim();
        Currency = currency ?? throw new SpaceDomainException("Currency is required for a space");
        InviteCode = inviteCode;
        IsActive = true;
        CreatedAt = createdAt;

        var creatorMembership = SpaceMembership.Create(creatorUserId, SpaceRole.Admin, createdAt);
        _members.Add(creatorMembership);
    }
    
    public bool HasMember(UserId userId) => Members.Any(m => m.UserId == userId);

    public SpaceMembership? GetMember(UserId userId) => Members.FirstOrDefault(m => m.UserId == userId);

    public bool IsAdmin(UserId userId) =>
        Members.FirstOrDefault(m => m.UserId == userId)?.Role == SpaceRole.Admin;

    public int AdminCount => Members.Count(m => m.Role == SpaceRole.Admin);
    
    public static Space Create(
        SpaceId id,
        string name,
        Currency currency,
        UserId creatorUserId,
        InviteCode inviteCode,
        IClock clock)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new SpaceDomainException("Space name cannot be empty");

        var createdAt = clock.UtcNow;

        var space = new Space(id, name, currency, inviteCode, creatorUserId, createdAt);
        
        space.RaiseDomainEvent(new SpaceCreated(
            id,
            name.Trim(),
            creatorUserId,
            inviteCode,
            createdAt
        ));
        
        return space;
    }
    
    public void Join(UserId userId, IClock clock)
    {
        if (HasMember(userId))
            throw new SpaceDomainException("User is already a member of this space");
            
        var joinedAt = clock.UtcNow;
        var membership = SpaceMembership.Create(userId, SpaceRole.Member, joinedAt);
        _members.Add(membership);
        
        RaiseDomainEvent(new SpaceMembershipChanged(
            Id,
            userId,
            MembershipChangeType.MemberJoined,
            SpaceRole.Member,
            joinedAt
        ));
    }
    
    public void PromoteMember(UserId userId, IClock clock)
    {
        var member = GetMember(userId);
        if (member == null)
            throw new SpaceDomainException("Cannot promote user who is not a member");
            
        if (member.Role == SpaceRole.Admin)
            return;
            
        member.ChangeRole(SpaceRole.Admin);
        
        RaiseDomainEvent(new SpaceMembershipChanged(
            Id,
            userId,
            MembershipChangeType.MemberPromoted,
            SpaceRole.Admin,
            clock.UtcNow
        ));
    }
    
    public void DemoteMember(UserId userId, IClock clock)
    {
        var member = GetMember(userId);
        if (member == null)
            throw new SpaceDomainException("Cannot demote user who is not a member");
            
        if (member.Role == SpaceRole.Member)
            return;
            
        if (AdminCount <= 1)
            throw new InvariantViolationException("Cannot demote the last admin");
            
        member.ChangeRole(SpaceRole.Member);
        
        RaiseDomainEvent(new SpaceMembershipChanged(
            Id,
            userId,
            MembershipChangeType.MemberDemoted,
            SpaceRole.Member,
            clock.UtcNow
        ));
    }
    
    public void RemoveMember(UserId userId, IClock clock)
    {
        var member = GetMember(userId);
        if (member == null)
            throw new SpaceDomainException("Cannot remove user who is not a member");
            
        if (member.Role == SpaceRole.Admin && AdminCount <= 1)
            throw new InvariantViolationException("Cannot remove the last admin");

        _members.Remove(member);
        
        RaiseDomainEvent(new SpaceMembershipChanged(
            Id,
            userId,
            MembershipChangeType.MemberRemoved,
            null,
            clock.UtcNow
        ));
    }

    public void UpdateName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Space name cannot be empty", nameof(newName));

        if (newName.Length > 100)
            throw new ArgumentException("Space name cannot exceed 100 characters", nameof(newName));

        Name = newName.Trim();
    }

    // A generous ceiling well within the numeric(18,2) column precision, so an out-of-range
    // input is rejected cleanly here instead of overflowing on write.
    public const decimal MaxRestockMinOrderValue = 1_000_000m;

    public void SetRestockSettings(int leadTimeDays, decimal minOrderValue)
    {
        if (leadTimeDays < 0 || leadTimeDays > 90)
            throw new SpaceDomainException("Restock lead time must be between 0 and 90 days");

        if (minOrderValue < 0 || minOrderValue > MaxRestockMinOrderValue)
            throw new SpaceDomainException($"Minimum order value must be between 0 and {MaxRestockMinOrderValue:0}");

        RestockLeadTimeDays = leadTimeDays;
        RestockMinOrderValue = decimal.Round(minOrderValue, 2, MidpointRounding.AwayFromZero);
    }

    public void Deactivate(IClock clock)
    {
        IsActive = false;

        RaiseDomainEvent(new SpaceDeactivated(
            Id,
            clock.UtcNow
        ));
    }

    public void RegenerateInviteCode(InviteCode newInviteCode)
    {
        ArgumentNullException.ThrowIfNull(newInviteCode);
        InviteCode = newInviteCode;
    }
}