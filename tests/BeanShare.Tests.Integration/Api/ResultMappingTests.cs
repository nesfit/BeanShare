using BeanShare.Api.Endpoints.Billing;
using BeanShare.Api.Endpoints.Common;
using BeanShare.Api.Endpoints.Settlement;
using BeanShare.Application.Common;
using FastEndpoints;
using FluentAssertions;
using MediatR;

namespace BeanShare.Tests.Integration.Api;

// Code round 3, G4/G11: failures keep their HTTP meaning on endpoints that shape their own errors.
// Pure unit tests (FastEndpoints' Factory, a canned mediator); no database or container.
public sealed class ResultMappingTests
{
    [Fact]
    public void TheSharedMapping_SendsConflictsAs409_AndTheLedgerCodesAs400()
    {
        ResultExtensions.GetStatusCode(new Error(Error.Codes.ConcurrencyConflict, "changed")).Should().Be(409);
        foreach (var code in new[]
                 {
                     Error.Codes.EntryLocked, Error.Codes.LineCarried, Error.Codes.LedgerStartLocked, Error.Codes.SettlementOrder,
                     Error.Codes.TreasurerRequired, Error.Codes.BeforeLedgerStart, Error.Codes.MemberHasBalance,
                     Error.Codes.PeriodNotDeletable
                 })
        {
            ResultExtensions.GetStatusCode(new Error(code, "x")).Should().Be(400, code);
        }

        ResultExtensions.IsConcurrencyConflict([new Error(Error.Codes.ValidationError, "x")]).Should().BeFalse();
        ResultExtensions.IsConcurrencyConflict([new Error(Error.Codes.ConcurrencyConflict, "x")]).Should().BeTrue();
    }

    [Fact]
    public async Task AnEndpointWithItsOwnFailureShape_SendsAConflictAs409WithItsCode()
    {
        var endpoint = Factory.Create<CloseBillingPeriodEndpoint>(
            new CannedMediator(Result.Failure(new Error(Error.Codes.ConcurrencyConflict, "The data changed meanwhile"))));

        await endpoint.HandleAsync(new CloseBillingPeriodRequest { BillingPeriodId = Guid.NewGuid() }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(409);
        endpoint.ValidationFailures.Should().ContainSingle("the shared mapping puts each code in the body")
            .Which.ErrorMessage.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    [Fact]
    public async Task AnEndpointWithItsOwnFailureShape_KeepsItOtherwise()
    {
        var endpoint = Factory.Create<CloseBillingPeriodEndpoint>(
            new CannedMediator(Result.Failure(new Error(Error.Codes.InvalidBillingPeriodState, "Not open"))));

        await endpoint.HandleAsync(new CloseBillingPeriodRequest { BillingPeriodId = Guid.NewGuid() }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SendingSettlementEmails_RefusedForANonAdmin_Is403()
    {
        var endpoint = Factory.Create<SendSettlementEmailsEndpoint>(new CannedMediator(Result<int>.Failure(Error.NotAdmin())));

        await endpoint.HandleAsync(new SendSettlementEmailsRequest { Id = Guid.NewGuid() }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(403);
    }

    /// <summary>Answers every Send with one canned response.</summary>
    private sealed class CannedMediator(object response) : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            Task.FromResult((TResponse)response);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => Task.FromResult<object?>(response);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
