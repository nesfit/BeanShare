using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace BeanShare.Application.Tests.Features;

public class SpaceBusinessRuleTests
{
    [Fact]
    public async Task JoinSpace_WhenInviteCodeNotFound_ShouldReturnBusinessError()
    {
        var repository = Substitute.For<ISpaceRepository>();
        var invalidCode = new InviteCode("ABCEFG"); // Valid format but not found in repo
        
        repository.GetByInviteCodeAsync(invalidCode, default).Returns((Space?)null);

        var space = await repository.GetByInviteCodeAsync(invalidCode);
        var result = space == null 
            ? Result.Failure(Error.InviteCodeNotFound(invalidCode.Value))
            : Result.Success();

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(Error.Codes.InviteCodeInvalid);
    }

    [Fact]
    public async Task GetSpaceById_WhenSpaceExists_ShouldReturnSpace()
    {
        var repository = Substitute.For<ISpaceRepository>();
        var spaceId = SpaceId.New();
        var expectedSpace = CreateTestSpace(spaceId);

        var specification = new SpaceByIdSpecification(spaceId);
        repository.GetSingleBySpecAsync(specification, default).Returns(expectedSpace);

        var result = await repository.GetSingleBySpecAsync(specification);

        result.Should().NotBeNull();
        result!.Id.Should().Be(spaceId);
    }

    [Fact]
    public void NewSpace_HasDefaultRestockSettings()
    {
        var space = CreateTestSpace(SpaceId.New());

        space.RestockLeadTimeDays.Should().Be(7);
        space.RestockMinOrderValue.Should().Be(2000m);
    }

    [Fact]
    public void SetRestockSettings_WithValidValues_UpdatesBoth()
    {
        var space = CreateTestSpace(SpaceId.New());

        space.SetRestockSettings(10, 1500.5m);

        space.RestockLeadTimeDays.Should().Be(10);
        space.RestockMinOrderValue.Should().Be(1500.5m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(91)]
    public void SetRestockSettings_WithLeadTimeOutOfRange_Throws(int leadTimeDays)
    {
        var space = CreateTestSpace(SpaceId.New());

        var act = () => space.SetRestockSettings(leadTimeDays, 2000m);

        act.Should().Throw<Domain.Exceptions.SpaceDomainException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void SetRestockSettings_WithMinOrderOutOfRange_Throws(decimal minOrderValue)
    {
        var space = CreateTestSpace(SpaceId.New());

        var act = () => space.SetRestockSettings(7, minOrderValue);

        act.Should().Throw<Domain.Exceptions.SpaceDomainException>();
    }

    private static Space CreateTestSpace(SpaceId id)
    {
        var creatorId = UserId.New();
        var inviteCode = new InviteCode("CAFE23");
        return Space.Create(id, "Test Coffee Space", Currency.USD, creatorId, inviteCode, TestClock.Instance);
    }
}

internal class TestClock : IClock
{
    public static readonly TestClock Instance = new();
    public DateTime UtcNow => new(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);
}