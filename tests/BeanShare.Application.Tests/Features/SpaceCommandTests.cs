using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.Settlement;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using MapsterMapper;
using NSubstitute;
using Xunit;

namespace BeanShare.Application.Tests.Features;

public class SpaceCommandTests
{
    [Fact]
    public async Task CreateSpace_WithValidName_ShouldSucceed()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var inviteCodeGenerator = Substitute.For<IInviteCodeGenerator>();
        var userContext = Substitute.For<IUserContext>();
        var clock = Substitute.For<IClock>();
        
        var userId = new UserId(Guid.NewGuid());
        var inviteCode = new InviteCode("CAFE23");
        
        userContext.CurrentUserId.Returns(userId);
        inviteCodeGenerator.GenerateAsync(default).Returns(inviteCode);
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new CreateSpaceCommandHandler(spaceRepository, inviteCodeGenerator, userContext, clock);
        var command = new CreateSpaceCommand("My Coffee Space", "USD");

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.InviteCode.Should().Be("CAFE23");
        await spaceRepository.Received(1).AddAsync(Arg.Any<Space>(), default);
    }

    [Fact]
    public async Task JoinSpace_WithValidCode_ShouldSucceed()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = Substitute.For<IClock>();
        
        var userId = new UserId(Guid.NewGuid());
        var creatorId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");
        
        userContext.CurrentUserId.Returns(userId);
        clock.UtcNow.Returns(DateTime.UtcNow);
        
        var space = Space.Create(spaceId, "Test Space", Currency.USD, creatorId, inviteCode, clock);
        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);

        var handler = new JoinSpaceCommandHandler(spaceRepository, userContext, clock);
        var command = new JoinSpaceCommand("CAFE23");

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.SpaceName.Should().Be("Test Space");
    }

    [Fact]
    public async Task PromoteMember_WithValidRequest_ShouldPromoteMember()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = Substitute.For<IClock>();

        var adminId = new UserId(Guid.NewGuid());
        var memberId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(adminId);
        clock.UtcNow.Returns(DateTime.UtcNow);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, clock);
        space.Join(memberId, clock);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);

        var handler = new PromoteMemberCommandHandler(spaceRepository, userContext, clock, TestUsers.ActiveRepository());
        var command = new PromoteMemberCommand(spaceId.Value, memberId.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be("Admin");
        space.IsAdmin(memberId).Should().BeTrue();
        await spaceRepository.Received(1).UpdateAsync(space, default);
    }

    [Fact]
    public async Task PromoteMember_WithNonAdminCaller_ShouldReturnUnauthorized()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = Substitute.For<IClock>();

        var adminId = new UserId(Guid.NewGuid());
        var memberId = new UserId(Guid.NewGuid());
        var unauthorizedUserId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(unauthorizedUserId);
        clock.UtcNow.Returns(DateTime.UtcNow);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, clock);
        space.Join(memberId, clock);
        space.Join(unauthorizedUserId, clock);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);

        var handler = new PromoteMemberCommandHandler(spaceRepository, userContext, clock, TestUsers.ActiveRepository());
        var command = new PromoteMemberCommand(spaceId.Value, memberId.Value);

        var result = await handler.Handle(command, default);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "INSUFFICIENT_PRIVILEGES");
        await spaceRepository.DidNotReceive().UpdateAsync(Arg.Any<Space>(), default);
    }

    [Fact]
    public async Task PromoteMember_WithNonMember_ShouldReturnMemberNotFound()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = Substitute.For<IClock>();

        var adminId = new UserId(Guid.NewGuid());
        var nonMemberId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(adminId);
        clock.UtcNow.Returns(DateTime.UtcNow);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, clock);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);

        var handler = new PromoteMemberCommandHandler(spaceRepository, userContext, clock, TestUsers.ActiveRepository());
        var command = new PromoteMemberCommand(spaceId.Value, nonMemberId.Value);

        var result = await handler.Handle(command, default);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "MEMBER_NOT_FOUND");
        await spaceRepository.DidNotReceive().UpdateAsync(Arg.Any<Space>(), default);
    }

    [Fact]
    public async Task PromoteMember_WithExistingAdmin_ShouldBeIdempotent()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = Substitute.For<IClock>();

        var adminId = new UserId(Guid.NewGuid());
        var targetAdminId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(adminId);
        clock.UtcNow.Returns(DateTime.UtcNow);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, clock);
        space.Join(targetAdminId, clock);
        space.PromoteMember(targetAdminId, clock);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);

        var handler = new PromoteMemberCommandHandler(spaceRepository, userContext, clock, TestUsers.ActiveRepository());
        var command = new PromoteMemberCommand(spaceId.Value, targetAdminId.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be("Admin");
        space.IsAdmin(targetAdminId).Should().BeTrue();
    }

    [Fact]
    public async Task DemoteMember_WithValidRequest_ShouldDemoteMember()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = Substitute.For<IClock>();

        var adminId = new UserId(Guid.NewGuid());
        var targetAdminId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(adminId);
        clock.UtcNow.Returns(DateTime.UtcNow);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, clock);
        space.Join(targetAdminId, clock);
        space.PromoteMember(targetAdminId, clock);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);

        var handler = new DemoteMemberCommandHandler(spaceRepository, userContext, clock);
        var command = new DemoteMemberCommand(spaceId.Value, targetAdminId.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be("Member");
        space.IsAdmin(targetAdminId).Should().BeFalse();
        await spaceRepository.Received(1).UpdateAsync(space, default);
    }

    [Fact]
    public async Task DemoteMember_WithLastAdmin_ShouldReturnLastAdminViolation()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = Substitute.For<IClock>();

        var adminId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(adminId);
        clock.UtcNow.Returns(DateTime.UtcNow);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, clock);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);

        var handler = new DemoteMemberCommandHandler(spaceRepository, userContext, clock);
        var command = new DemoteMemberCommand(spaceId.Value, adminId.Value);

        var result = await handler.Handle(command, default);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "LAST_ADMIN_PROTECTION");
        await spaceRepository.DidNotReceive().UpdateAsync(Arg.Any<Space>(), default);
    }

    [Fact]
    public async Task DemoteMember_WithExistingMember_ShouldBeIdempotent()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var clock = Substitute.For<IClock>();

        var adminId = new UserId(Guid.NewGuid());
        var memberId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(adminId);
        clock.UtcNow.Returns(DateTime.UtcNow);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, clock);
        space.Join(memberId, clock);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);

        var handler = new DemoteMemberCommandHandler(spaceRepository, userContext, clock);
        var command = new DemoteMemberCommand(spaceId.Value, memberId.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be("Member");
        space.IsAdmin(memberId).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveMember_WithSelfRemoval_ShouldSucceed()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var settlementRepository = Substitute.For<ISettlementRepository>();
        var userContext = Substitute.For<IUserContext>();
        var mapper = Substitute.For<IMapper>();
        var clock = Substitute.For<IClock>();

        var adminId = new UserId(Guid.NewGuid());
        var memberId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(memberId); // Member removing themselves
        clock.UtcNow.Returns(DateTime.UtcNow);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, clock);
        space.Join(memberId, clock);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);
        settlementRepository.GetBySpaceIdAsync(spaceId, default).Returns(new List<Domain.Aggregates.Settlement.Settlement>());

        var handler = new RemoveMemberCommandHandler(spaceRepository, settlementRepository, EmptyLedger.SnapshotService(clock), userContext, mapper, clock, TestUsers.ActiveRepository());
        var command = new RemoveMemberCommand(spaceId.Value, memberId.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Action.Should().Be("removed");
        result.Value.UserId.Should().Be(memberId.Value);
        await spaceRepository.Received(1).UpdateAsync(space, default);
    }

    [Fact]
    public async Task RemoveMember_WithSelfRemovalAsLastAdmin_ShouldReturnError()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var settlementRepository = Substitute.For<ISettlementRepository>();
        var userContext = Substitute.For<IUserContext>();
        var mapper = Substitute.For<IMapper>();
        var clock = Substitute.For<IClock>();

        var adminId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(adminId); // Admin trying to remove themselves (last admin)
        clock.UtcNow.Returns(DateTime.UtcNow);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, clock);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);
        settlementRepository.GetBySpaceIdAsync(spaceId, default).Returns(new List<Domain.Aggregates.Settlement.Settlement>());

        var handler = new RemoveMemberCommandHandler(spaceRepository, settlementRepository, EmptyLedger.SnapshotService(clock), userContext, mapper, clock, TestUsers.ActiveRepository());
        var command = new RemoveMemberCommand(spaceId.Value, adminId.Value);

        var result = await handler.Handle(command, default);

        result.IsFailure.Should().BeTrue();
        result.Errors.First().Code.Should().Be(Error.Codes.CannotRemoveLastAdmin);
        await spaceRepository.DidNotReceive().UpdateAsync(Arg.Any<Space>(), default);
    }

    [Fact]
    public async Task RemoveMember_WithNonAdminRemovingOther_ShouldReturnError()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var settlementRepository = Substitute.For<ISettlementRepository>();
        var userContext = Substitute.For<IUserContext>();
        var mapper = Substitute.For<IMapper>();
        var clock = Substitute.For<IClock>();

        var adminId = new UserId(Guid.NewGuid());
        var memberId = new UserId(Guid.NewGuid());
        var otherMemberId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(memberId); // Non-admin trying to remove someone else
        clock.UtcNow.Returns(DateTime.UtcNow);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, clock);
        space.Join(memberId, clock);
        space.Join(otherMemberId, clock);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);
        settlementRepository.GetBySpaceIdAsync(spaceId, default).Returns(new List<Domain.Aggregates.Settlement.Settlement>());

        var handler = new RemoveMemberCommandHandler(spaceRepository, settlementRepository, EmptyLedger.SnapshotService(clock), userContext, mapper, clock, TestUsers.ActiveRepository());
        var command = new RemoveMemberCommand(spaceId.Value, otherMemberId.Value);

        var result = await handler.Handle(command, default);

        result.IsFailure.Should().BeTrue();
        result.Errors.First().Code.Should().Be(Error.Codes.InsufficientPrivileges);
        await spaceRepository.DidNotReceive().UpdateAsync(Arg.Any<Space>(), default);
    }

    [Fact]
    public async Task RegenerateInviteCode_WithValidAdmin_ShouldSucceed()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var inviteCodeGenerator = Substitute.For<IInviteCodeGenerator>();
        var mapper = Substitute.For<IMapper>();

        var adminId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var oldInviteCode = new InviteCode("CAFE23");
        var newInviteCode = new InviteCode("BREW45");

        userContext.CurrentUserId.Returns(adminId);
        inviteCodeGenerator.GenerateAsync(Arg.Any<CancellationToken>()).Returns(newInviteCode);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, oldInviteCode, TestClock.Instance);
        var expectedDto = new SpaceDto
        {
            Id = spaceId,
            Name = "Test Space",
            CurrencyCode = "USD",
            InviteCode = "BREW45",
            IsActive = true,
            CreatedBy = adminId,
            CreatedAt = TestClock.Instance.UtcNow,
            MemberCount = 1,
            Members = new List<MembershipDto>()
        };

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);
        mapper.Map<SpaceDto>(space).Returns(expectedDto);

        var handler = new RegenerateInviteCodeCommandHandler(spaceRepository, userContext, inviteCodeGenerator, mapper);
        var command = new RegenerateInviteCodeCommand(spaceId.Value);

        var result = await handler.Handle(command, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.InviteCode.Should().Be("BREW45");
        space.InviteCode.Value.Should().Be("BREW45");
        await spaceRepository.Received(1).UpdateAsync(space, default);
    }

    [Fact]
    public async Task RegenerateInviteCode_WithNonAdmin_ShouldReturnError()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var inviteCodeGenerator = Substitute.For<IInviteCodeGenerator>();
        var mapper = Substitute.For<IMapper>();

        var adminId = new UserId(Guid.NewGuid());
        var memberId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(memberId); // Non-admin user

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, TestClock.Instance);
        space.Join(memberId, TestClock.Instance);

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);

        var handler = new RegenerateInviteCodeCommandHandler(spaceRepository, userContext, inviteCodeGenerator, mapper);
        var command = new RegenerateInviteCodeCommand(spaceId.Value);

        var result = await handler.Handle(command, default);

        result.IsFailure.Should().BeTrue();
        result.Errors.First().Code.Should().Be(Error.Codes.InsufficientPrivileges);
        await spaceRepository.DidNotReceive().UpdateAsync(Arg.Any<Space>(), default);
    }

    [Fact]
    public async Task RegenerateInviteCode_WithDeactivatedSpace_ShouldReturnError()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var inviteCodeGenerator = Substitute.For<IInviteCodeGenerator>();
        var mapper = Substitute.For<IMapper>();

        var adminId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(adminId);

        var space = Space.Create(spaceId, "Test Space", Currency.USD, adminId, inviteCode, TestClock.Instance);
        space.Deactivate(TestClock.Instance); // Deactivate the space

        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);

        var handler = new RegenerateInviteCodeCommandHandler(spaceRepository, userContext, inviteCodeGenerator, mapper);
        var command = new RegenerateInviteCodeCommand(spaceId.Value);

        var result = await handler.Handle(command, default);

        result.IsFailure.Should().BeTrue();
        result.Errors.First().Code.Should().Be(Error.Codes.ValidationError);
        await spaceRepository.DidNotReceive().UpdateAsync(Arg.Any<Space>(), default);
    }
}