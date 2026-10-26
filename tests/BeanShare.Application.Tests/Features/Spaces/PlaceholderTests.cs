using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Notifications.EventHandlers;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Application.Features.Spaces.Queries;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Events;
using BeanShare.Domain.ValueObjects;
using MapsterMapper;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Spaces;

// T-PLH-01 (D21, DS-14), T-PLH-06 (DS-33) and the placeholder part of T-DOM-04.
public sealed class PlaceholderTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan31 = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));

    private Task<Result<MembershipDto>> CreateAsync(string name, string? email = null) =>
        new CreatePlaceholderMemberCommandHandler(_world.SpaceRepository, _world.UserRepository, _world.Clock)
            .Handle(new CreatePlaceholderMemberCommand(_world.Space.Id.Value, name, email), default);

    [Fact]
    public async Task Create_WithoutAnEmail_GetsTheSentinel_WhichIsNeverShown()
    {
        var result = await CreateAsync("  Old Pete ");

        result.IsSuccess.Should().BeTrue();
        result.Value.IsPlaceholder.Should().BeTrue();
        result.Value.Email.Should().BeEmpty();
        result.Value.UserName.Should().Be("Old Pete");
        result.Value.Role.Should().Be(SpaceRole.Member.ToString());

        var user = _world.UserOf(new UserId(result.Value.UserId));
        user.IsPlaceholder.Should().BeTrue();
        user.Email.Should().Be(User.SentinelEmailFor(user.Id));
        user.HasDeliverableEmail.Should().BeFalse();
        user.PasswordHash.Should().BeNull();
        _world.Space.HasMember(user.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Create_WithAnEmail_KeepsItNormalised()
    {
        var result = await CreateAsync("Pete", "  Pete@Example.ORG ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("pete@example.org");
    }

    [Theory]
    [InlineData("ADMIN@example.org")]
    [InlineData("admin@example.org")]
    public async Task Create_WithAnEmailInUse_FailsWithEmailInUse_CaseInsensitively(string email)
    {
        var members = _world.Space.Members.Count;

        var result = await CreateAsync("Copy", email);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.EmailInUse);
        _world.Space.Members.Should().HaveCount(members);
    }

    [Fact]
    public async Task Create_WithTheEmailOfAnotherPlaceholder_FailsWithEmailInUse()
    {
        (await CreateAsync("Pete", "pete@example.org")).IsSuccess.Should().BeTrue();

        (await CreateAsync("Peter", "PETE@example.org")).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(Error.Codes.EmailInUse);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutAName_IsRefused(string name)
    {
        (await CreateAsync(name)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ValidationError);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b@c")]
    [InlineData("x@placeholder.invalid")]
    public async Task Create_WithABadOrReservedEmail_IsRefused(string email)
    {
        (await CreateAsync("Pete", email)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ValidationError);
    }

    [Fact]
    public async Task Update_ChangesNameAndEmail_AndClearingTheEmailGivesTheSentinelBack()
    {
        var id = _world.AddPlaceholder("Pete", "pete@example.org");
        var handler = new UpdatePlaceholderMemberCommandHandler(_world.SpaceRepository, _world.UserRepository, _world.UserService);

        var renamed = await handler.Handle(new UpdatePlaceholderMemberCommand(_world.Space.Id.Value, id.Value, "Peter", "peter@example.org"), default);
        renamed.IsSuccess.Should().BeTrue();
        _world.UserOf(id).Email.Should().Be("peter@example.org");
        _world.UserService.Received().Evict(id, "pete@example.org", "peter@example.org");

        var cleared = await handler.Handle(new UpdatePlaceholderMemberCommand(_world.Space.Id.Value, id.Value, "Peter"), default);
        cleared.IsSuccess.Should().BeTrue();
        cleared.Value.Email.Should().BeEmpty();
        _world.UserOf(id).HasDeliverableEmail.Should().BeFalse();

        var taken = await handler.Handle(new UpdatePlaceholderMemberCommand(_world.Space.Id.Value, id.Value, "Peter", "Admin@example.org"), default);
        taken.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.EmailInUse);

        var real = await handler.Handle(new UpdatePlaceholderMemberCommand(_world.Space.Id.Value, _world.Admin.Value, "Boss"), default);
        real.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.NotPlaceholder);
    }

    [Fact]
    public async Task APlaceholder_CannotBePromoted()
    {
        var id = _world.AddPlaceholder("Pete");

        var result = await new PromoteMemberCommandHandler(_world.SpaceRepository, _world.UserContext, _world.Clock, _world.UserRepository)
            .Handle(new PromoteMemberCommand(_world.Space.Id.Value, id.Value), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.CannotPromoteMember);
        _world.Space.IsAdmin(id).Should().BeFalse();
    }

    [Fact]
    public async Task APlaceholder_CannotBeTheTreasurer_EvenIfItWereAnAdmin()
    {
        var id = _world.AddPlaceholder("Pete");
        _world.Space.PromoteMember(id, _world.Clock);

        var result = await new UpdateLedgerSettingsHandler(
                _world.SpaceRepository, _world.SettlementRepository, _world.UserService, _world.UserRepository, _world.BillingPeriodRepository)
            .Handle(new UpdateLedgerSettingsCommand(_world.Space.Id.Value, id.Value, null), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ValidationError);
        _world.Space.TreasurerUserId.Should().BeNull();
    }

    [Fact]
    public async Task PlaceholderAdded_SendsNoNotification_AndPlaceholdersGetNoneForANewMember()
    {
        var placeholder = _world.AddPlaceholder("Pete");
        var alice = _world.AddMember("Alice");
        var handler = new MembershipChangedNotificationHandler(_world.NotificationRepository, _world.SpaceRepository, _world.UserRepository, _world.Clock);

        await handler.Handle(new SpaceMembershipChanged(_world.Space.Id, placeholder, MembershipChangeType.PlaceholderAdded, SpaceRole.Member, _world.Clock.UtcNow), default);
        _world.Notifications.Should().BeEmpty();

        await handler.Handle(new SpaceMembershipChanged(_world.Space.Id, alice, MembershipChangeType.MemberJoined, SpaceRole.Member, _world.Clock.UtcNow), default);
        _world.Notifications.Should().ContainSingle().Which.UserId.Should().Be(_world.Admin);
    }

    [Fact]
    public async Task SettlementAndClosedPeriodNotifications_SkipPlaceholders()
    {
        var placeholder = _world.AddPlaceholder("Pete");
        var alice = _world.AddMember("Alice");

        await new SettlementGeneratedNotificationHandler(_world.NotificationRepository, _world.SpaceRepository, _world.Clock, _world.UserService)
            .Handle(new SettlementGenerated(SettlementId.New(), _world.Space.Id, BillingPeriodId.New(), _world.Admin, _world.Clock.UtcNow), default);
        await new BillingPeriodClosedNotificationHandler(_world.NotificationRepository, _world.SpaceRepository, _world.Clock, _world.UserService)
            .Handle(new BillingPeriodClosed(BillingPeriodId.New(), _world.Space.Id, _world.Admin, _world.Clock.UtcNow), default);

        _world.Notifications.Should().HaveCount(2).And.OnlyContain(n => n.UserId == alice);
        _world.Notifications.Should().NotContain(n => n.UserId == placeholder);
    }

    [Fact]
    public async Task SettlementDtos_ShowAnEmptyEmail_ForASentinel()
    {
        _world.Space.SetTreasurer(_world.Admin);
        var placeholder = _world.AddPlaceholder("Pete");
        _world.AddEntry(placeholder, -20m, Jan1);
        var period = _world.AddPeriod("January", Jan1, Jan31);

        var preview = await _world.PreviewAsync(period);
        preview.IsSuccess.Should().BeTrue();
        preview.Value.Lines.Single(l => l.UserId == placeholder.Value).UserEmail.Should().BeEmpty();

        (await _world.GenerateAsync(period)).IsSuccess.Should().BeTrue();
        var dto = await _world.GetSettlementAsync(_world.SettlementOf(period));
        dto.Value.Lines.Single(l => l.UserId == placeholder.Value).UserEmail.Should().BeEmpty();
        dto.Value.Lines.Single(l => l.UserId == _world.Admin.Value).UserEmail.Should().Be("admin@example.org");
    }

    [Fact]
    public async Task SpaceDetails_MarkPlaceholders_WithoutTheSentinel_AndHideMergedOnes()
    {
        var pete = _world.AddPlaceholder("Pete");
        var gone = _world.AddPlaceholder("Gone");
        var alice = _world.AddMember("Alice");
        (await _world.MergeAsync(gone, alice)).IsSuccess.Should().BeTrue();

        var mapper = Substitute.For<IMapper>();
        mapper.Map<SpaceDto>(_world.Space).Returns(new SpaceDto
        {
            Id = _world.Space.Id.Value,
            Name = _world.Space.Name,
            CurrencyCode = "CZK",
            InviteCode = "KAVAXY",
            IsActive = true,
            CreatedBy = _world.Admin.Value,
            CreatedAt = _world.Clock.UtcNow,
            MemberCount = _world.Space.Members.Count,
            Members = _world.Space.Members
                .Select(m => new MembershipDto { UserId = m.UserId.Value, Email = "", Role = m.Role.ToString(), JoinedAt = m.JoinedAt })
                .ToList()
        });

        var result = await new GetSpaceByIdQueryHandler(_world.SpaceRepository, _world.UserContext, _world.UserService, mapper, _world.UserRepository)
            .Handle(new GetSpaceByIdQuery(_world.Space.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.MemberCount.Should().Be(3);
        result.Value.Members.Should().NotContain(m => m.UserId == gone.Value);
        var shown = result.Value.Members.Single(m => m.UserId == pete.Value);
        shown.IsPlaceholder.Should().BeTrue();
        shown.Email.Should().BeEmpty();
    }
}
