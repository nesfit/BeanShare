using BeanShare.Application.Abstractions;
using BeanShare.Application.Behaviors;
using BeanShare.Application.Common;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Exceptions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BeanShare.Application.Tests.Pipeline;

// Concurrency conflicts become CONCURRENCY_CONFLICT results, never exceptions.
public sealed class ConcurrencyBehaviorTests
{
    public sealed record PlainCommand : ICommand<Result<int>>;

    public sealed record RetryableCommand : ICommand<Result<int>>, IRetryOnConcurrencyConflict;

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();

    public ConcurrencyBehaviorTests()
    {
        _dispatcher.CollectDomainEvents().Returns(new List<IDomainEvent>());
    }

    private static ConcurrencyConflictException Conflict(params Type[] types) =>
        new(types, new InvalidOperationException("token mismatch"));

    [Fact]
    public async Task UnitOfWork_TurnsAConflictOnSaveIntoAFailure()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(Conflict(typeof(ConsumptionEntry)));
        var behavior = new UnitOfWorkBehavior<PlainCommand, Result<int>>(_unitOfWork, _dispatcher);

        var result = await behavior.Handle(new PlainCommand(), () => Task.FromResult(Result<int>.Success(1)), default);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        await _dispatcher.DidNotReceive().DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnitOfWork_MapsAnEmailIndexConflictToEmailInUse()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException([typeof(User)], new InvalidOperationException(), Error.Codes.EmailInUse));
        var behavior = new UnitOfWorkBehavior<PlainCommand, Result<int>>(_unitOfWork, _dispatcher);

        var result = await behavior.Handle(new PlainCommand(), () => Task.FromResult(Result<int>.Success(1)), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.EmailInUse);
    }

    [Fact]
    public async Task UnitOfWork_LetsARetryableConflictThroughToTheRetryBehavior()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(Conflict(typeof(Purchase)));
        var behavior = new UnitOfWorkBehavior<RetryableCommand, Result<int>>(_unitOfWork, _dispatcher);

        var act = () => behavior.Handle(new RetryableCommand(), () => Task.FromResult(Result<int>.Success(1)), default);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Transaction_RollsBackWhenTheCommandFailedWithAConflict()
    {
        var behavior = new TransactionBehavior<PlainCommand, Result<int>>(_unitOfWork);

        var result = await behavior.Handle(new PlainCommand(),
            () => Task.FromResult(Result<int>.Failure(Error.ConcurrencyConflict())), default);

        result.Errors[0].Code.Should().Be(Error.Codes.ConcurrencyConflict);
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transaction_TurnsAConflictOnTheCommitPathIntoAFailure()
    {
        _unitOfWork.CommitTransactionAsync(Arg.Any<CancellationToken>()).ThrowsAsync(Conflict(typeof(Notification)));
        var behavior = new TransactionBehavior<PlainCommand, Result<int>>(_unitOfWork);

        var result = await behavior.Handle(new PlainCommand(), () => Task.FromResult(Result<int>.Success(1)), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
        await _unitOfWork.Received().RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retry_RerunsACommandThatLostARaceOnBagCountersOnly()
    {
        var behavior = new ConcurrencyRetryBehavior<RetryableCommand, Result<int>>(_unitOfWork);
        var attempts = 0;

        var result = await behavior.Handle(new RetryableCommand(), () =>
        {
            attempts++;
            if (attempts == 1)
                throw Conflict(typeof(Purchase));
            return Task.FromResult(Result<int>.Success(attempts));
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
        await _unitOfWork.Received(1).ResetAsync(Arg.Any<CancellationToken>());
    }

    // Every new cup bumps the space's ledger token, so two cups logged at once conflict there; that is retried too.
    [Fact]
    public async Task Retry_RerunsACupThatLostARaceOnTheSpacesLedgerToken()
    {
        var behavior = new ConcurrencyRetryBehavior<RetryableCommand, Result<int>>(_unitOfWork);
        var attempts = 0;

        var result = await behavior.Handle(new RetryableCommand(), () =>
        {
            attempts++;
            if (attempts == 1)
                throw Conflict(typeof(Purchase), typeof(BeanShare.Domain.Aggregates.Space.Space));
            return Task.FromResult(Result<int>.Success(attempts));
        }, default);

        result.Value.Should().Be(2);
    }

    [Fact]
    public async Task Retry_NeverRetriesAConflictOnTheCupItself()
    {
        var behavior = new ConcurrencyRetryBehavior<RetryableCommand, Result<int>>(_unitOfWork);
        var attempts = 0;

        var result = await behavior.Handle(new RetryableCommand(), () =>
        {
            attempts++;
            throw Conflict(typeof(Purchase), typeof(ConsumptionEntry));
        }, default);

        attempts.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Retry_GivesUpAfterTwoRetries()
    {
        var behavior = new ConcurrencyRetryBehavior<RetryableCommand, Result<int>>(_unitOfWork);
        var attempts = 0;

        var result = await behavior.Handle(new RetryableCommand(), () =>
        {
            attempts++;
            throw Conflict(typeof(Purchase));
        }, default);

        attempts.Should().Be(1 + ConcurrencyRetryBehavior<RetryableCommand, Result<int>>.MaxRetries);
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Retry_IgnoresUnmarkedRequests()
    {
        var behavior = new ConcurrencyRetryBehavior<PlainCommand, Result<int>>(_unitOfWork);

        var act = () => behavior.Handle(new PlainCommand(), () => throw Conflict(typeof(Purchase)), default);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        await _unitOfWork.DidNotReceive().ResetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExceptionHandling_SurfacesTheLedgerErrorCode()
    {
        var behavior = new ExceptionHandlingBehavior<PlainCommand, Result<int>>();

        var result = await behavior.Handle(new PlainCommand(),
            () => throw new LedgerDomainException(LedgerDomainException.LineCarried, "carried"), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be("LINE_CARRIED");
    }
}
