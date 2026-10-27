using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Spaces.Dtos;
using BeanShare.Application.Features.Spaces.Queries;
using BeanShare.Application.Services;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using MapsterMapper;
using NSubstitute;
using Xunit;

namespace BeanShare.Application.Tests.Features;

public class SpaceQueryTests
{
    [Fact]
    public async Task GetSpaceById_WithValidId_ShouldReturnSpace()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var userService = Substitute.For<IUserService>();
        var mapper = Substitute.For<IMapper>();
        var clock = Substitute.For<IClock>();

        var userId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");

        userContext.CurrentUserId.Returns(userId);
        clock.UtcNow.Returns(DateTime.UtcNow);
        userService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), default).Returns(new List<User>());

        var space = Space.Create(spaceId, "Test Space", Currency.USD, userId, inviteCode, clock);
        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(space);

        var spaceDto = new SpaceDto
        {
            Id = spaceId,
            Name = "Test Space",
            CurrencyCode = "USD",
            InviteCode = "CAFE23",
            IsActive = true,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            MemberCount = 1,
            Members = new List<MembershipDto>()
        };
        mapper.Map<SpaceDto>(space).Returns(spaceDto);

        var handler = new GetSpaceByIdQueryHandler(spaceRepository, userContext, userService, mapper, TestUsers.ActiveRepository());
        var query = new GetSpaceByIdQuery(spaceId);

        var result = await handler.Handle(query, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Test Space");
        result.Value.InviteCode.Should().Be("CAFE23");
    }

    [Fact]
    public async Task GetSpaceById_WithNonExistentId_ShouldReturnError()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var userService = Substitute.For<IUserService>();
        var mapper = Substitute.For<IMapper>();

        var spaceId = SpaceId.New();
        spaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns((Space?)null);

        var handler = new GetSpaceByIdQueryHandler(spaceRepository, userContext, userService, mapper, TestUsers.ActiveRepository());
        var query = new GetSpaceByIdQuery(spaceId);

        var result = await handler.Handle(query, default);

        result.IsFailure.Should().BeTrue();
        result.Errors.First().Code.Should().Be(Error.Codes.SpaceNotFound);
    }

    [Fact]
    public async Task GetUserSpaces_WithValidUser_ShouldReturnSpaces()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var mapper = Substitute.For<IMapper>();
        var clock = Substitute.For<IClock>();
        
        var userId = new UserId(Guid.NewGuid());
        var spaceId = SpaceId.New();
        var inviteCode = new InviteCode("CAFE23");
        
        userContext.CurrentUserId.Returns(userId);
        clock.UtcNow.Returns(DateTime.UtcNow);
        
        var space = Space.Create(spaceId, "Test Space", Currency.USD, userId, inviteCode, clock);
        var spaces = new List<Space> { space };
        
        spaceRepository.GetBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(spaces);
        
        var spaceSummary = new SpaceSummaryDto 
        { 
            Id = spaceId, 
            Name = "Test Space",
            InviteCode = "CAFE23",
            MemberCount = 1,
            CreatedAt = DateTime.UtcNow
        };
        mapper.Map<IReadOnlyList<SpaceSummaryDto>>(spaces).Returns(new List<SpaceSummaryDto> { spaceSummary });

        var handler = new GetUserSpacesQueryHandler(spaceRepository, userContext, mapper, Substitute.For<IUserService>());
        var query = new GetUserSpacesQuery();

        var result = await handler.Handle(query, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Spaces.Should().HaveCount(1);
        result.Value.Spaces.First().Name.Should().Be("Test Space");
    }
    // The space list counts people who use the app; placeholders, merged ones included, are left out.
    [Fact]
    public async Task GetUserSpaces_CountsMembersWithoutPlaceholders()
    {
        var spaceRepository = Substitute.For<ISpaceRepository>();
        var userContext = Substitute.For<IUserContext>();
        var userService = Substitute.For<IUserService>();
        var mapper = Substitute.For<IMapper>();
        var clock = TestClock.Instance;
        var admin = UserId.New();
        var member = UserId.New();
        var pete = User.CreatePlaceholder("Old Pete", null, clock.UtcNow);
        var olga = User.CreatePlaceholder("Old Olga", null, clock.UtcNow);
        olga.MarkMergedInto(member);

        var space = Space.Create(SpaceId.New(), "Kava", Currency.USD, admin, new InviteCode("CAFE23"), clock);
        space.Join(member, clock);
        space.AddPlaceholderMember(pete.Id, clock);
        space.AddPlaceholderMember(olga.Id, clock);
        userContext.CurrentUserId.Returns(admin);
        spaceRepository.GetBySpecAsync(Arg.Any<ISpec<Space>>(), default).Returns(new List<Space> { space });
        mapper.Map<IReadOnlyList<SpaceSummaryDto>>(Arg.Any<object>()).Returns(new List<SpaceSummaryDto>
        {
            new() { Id = space.Id.Value, Name = "Kava", InviteCode = "CAFE23", MemberCount = space.Members.Count, CreatedAt = clock.UtcNow }
        });
        userService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), Arg.Any<CancellationToken>()).Returns(new List<User> { pete, olga });

        var result = await new GetUserSpacesQueryHandler(spaceRepository, userContext, mapper, userService).Handle(new GetUserSpacesQuery(), default);

        result.Value.Spaces.Single().MemberCount.Should().Be(2);
    }
}
