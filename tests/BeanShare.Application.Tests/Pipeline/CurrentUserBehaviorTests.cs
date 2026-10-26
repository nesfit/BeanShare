using BeanShare.Application.Abstractions;
using BeanShare.Application.Behaviors;
using BeanShare.Application.Common;
using BeanShare.Application.Features.History.Dtos;
using BeanShare.Application.Features.History.Queries.GetSpaceHistory;
using BeanShare.Application.Features.Settlement.Commands.ConfirmPayment;
using BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Features.Spaces.Queries;
using BeanShare.Application.Features.Users.Queries.GetPendingPlaceholderLink;
using BeanShare.Domain.Common;
using MediatR;
using NSubstitute;

namespace BeanShare.Application.Tests.Pipeline;

// T-PLH-04 (DS-30, U8): an unknown, inactive or placeholder identity cannot act.
public sealed class CurrentUserBehaviorTests
{
    private readonly IUserContext _userContext = Substitute.For<IUserContext>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly UserId _userId = UserId.New();

    public CurrentUserBehaviorTests()
    {
        _userContext.CurrentUserId.Returns(_userId);
    }

    public static TheoryData<string> RefusedStatuses() => new() { "missing", "inactive", "placeholder", "merged" };

    private void GivenStatus(string kind) =>
        _userRepository.GetAccessStatusAsync(_userId, Arg.Any<CancellationToken>()).Returns(kind switch
        {
            "missing" => UserAccessStatus.Missing,
            "inactive" => new UserAccessStatus(true, false, false, null),
            "placeholder" => new UserAccessStatus(true, true, true, null),
            "merged" => new UserAccessStatus(true, true, true, UserId.New()),
            _ => new UserAccessStatus(true, true, false, null)
        });

    private async Task<(TResponse Response, bool NextCalled)> RunAsync<TRequest, TResponse>(TRequest request, TResponse fromNext)
        where TRequest : notnull, IRequest<TResponse>
    {
        var called = false;
        var behavior = new CurrentUserBehavior<TRequest, TResponse>(_userContext, _userRepository);
        var response = await behavior.Handle(request, () =>
        {
            called = true;
            return Task.FromResult(fromNext);
        }, default);
        return (response, called);
    }

    [Theory]
    [MemberData(nameof(RefusedStatuses))]
    public async Task EveryCommand_IsRefused(string kind)
    {
        GivenStatus(kind);

        var create = await RunAsync(new CreateSpaceCommand("Kava", "CZK"), Result<CreateSpaceResult>.Success(new CreateSpaceResult(Guid.NewGuid(), "X")));
        var join = await RunAsync(new JoinSpaceCommand("KAVAXY"), Result<JoinSpaceResult>.Success(new JoinSpaceResult(Guid.NewGuid(), "Kava")));
        var confirm = await RunAsync(new ConfirmPaymentCommand(Guid.NewGuid(), Guid.NewGuid()), Result.Success());
        var generate = await RunAsync(new GenerateSettlementCommand(Guid.NewGuid()), Result<BeanShare.Application.Features.Settlement.Dtos.SettlementDto>.Failure(new Error("UNUSED", "unused")));

        foreach (var (errors, called) in new[]
                 {
                     (create.Response.Errors, create.NextCalled), (join.Response.Errors, join.NextCalled),
                     (confirm.Response.Errors, confirm.NextCalled), (generate.Response.Errors, generate.NextCalled)
                 })
        {
            called.Should().BeFalse();
            errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.Unauthenticated);
        }
    }

    [Theory]
    [MemberData(nameof(RefusedStatuses))]
    public async Task AnIAuthorizeQuery_IsRefused(string kind)
    {
        GivenStatus(kind);

        var (response, called) = await RunAsync(new GetSpaceHistoryQuery(Guid.NewGuid()), Result<SpaceHistoryDto>.Failure(new Error("UNUSED", "unused")));

        called.Should().BeFalse();
        response.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.Unauthenticated);
    }

    [Theory]
    [MemberData(nameof(RefusedStatuses))]
    public async Task APlainQuery_PassesThrough(string kind)
    {
        GivenStatus(kind);

        var (_, pendingCalled) = await RunAsync<GetPendingPlaceholderLinkQuery, PendingPlaceholderLinkDto?>(new GetPendingPlaceholderLinkQuery(), null);
        var (_, spacesCalled) = await RunAsync(new GetUserSpacesQuery(), Result<GetUserSpacesResult>.Failure(new Error("UNUSED", "unused")));

        pendingCalled.Should().BeTrue();
        spacesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task AnActiveRealUser_Passes()
    {
        GivenStatus("active");

        var (response, called) = await RunAsync(new ConfirmPaymentCommand(Guid.NewGuid(), Guid.NewGuid()), Result.Success());

        called.Should().BeTrue();
        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AnUnauthenticatedContext_PassesUnchanged_WithoutALookup()
    {
        _userContext.CurrentUserId.Returns(_ => throw new InvalidOperationException("User is not authenticated"));

        var (response, called) = await RunAsync(new ConfirmPaymentCommand(Guid.NewGuid(), Guid.NewGuid()), Result.Success());

        called.Should().BeTrue();
        response.IsSuccess.Should().BeTrue();
        await _userRepository.DidNotReceiveWithAnyArgs().GetAccessStatusAsync(default, default);
    }
}
