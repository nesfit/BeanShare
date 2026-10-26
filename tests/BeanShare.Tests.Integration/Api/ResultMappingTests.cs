using BeanShare.Api.Endpoints.Billing;
using BeanShare.Api.Endpoints.Common;
using BeanShare.Api.Endpoints.Settlement;
using BeanShare.Api.Endpoints.CoffeeStock;
using BeanShare.Api.Endpoints.Spaces;
using BeanShare.Api.Endpoints.Settlements;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Contracts.Spaces;
using BeanShare.Application.Common;
using FastEndpoints;
using System.Text.Json;
using FluentAssertions;
using MediatR;

namespace BeanShare.Tests.Integration.Api;

// Code round 3, G4/G11: failures keep their HTTP meaning on endpoints that shape their own errors.
// Pure unit tests (FastEndpoints' Factory, a canned mediator); no database or container.
[Collection(FastEndpointsFactoryCollection.Name)]
public sealed class ResultMappingTests
{
    public ResultMappingTests() => FastEndpointsFactoryIsolation.ResetResolver();

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

    // T-INT-11 (I8): the newly wired stock and space endpoints answer a conflict with 409 and the code.
    [Fact]
    public async Task SetCurrentlyUsedStockLevel_SendsAConflictAs409WithItsCode()
    {
        var endpoint = Factory.Create<SetCurrentlyUsedStockLevelEndpoint>(
            ctx =>
            {
                ctx.Request.RouteValues["spaceId"] = Guid.NewGuid().ToString();
                ctx.Request.RouteValues["stockLevelId"] = Guid.NewGuid().ToString();
            },
            new CannedMediator(Result<bool>.Failure(new Error(Error.Codes.ConcurrencyConflict, "The data changed meanwhile"))));

        await endpoint.HandleAsync(default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(409);
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    [Fact]
    public async Task RegenerateInviteCode_SendsAConflictAs409WithItsCode()
    {
        var spaceId = Guid.NewGuid();
        var conflict = Factory.Create<RegenerateInviteCodeEndpoint>(
            ctx => ctx.Request.RouteValues["spaceId"] = spaceId.ToString(),
            new CannedMediator(Result<BeanShare.Application.Features.Spaces.Dtos.SpaceDto>.Failure(new Error(Error.Codes.ConcurrencyConflict, "changed"))));

        await conflict.HandleAsync(new RegenerateInviteCodeRequest { SpaceId = spaceId }, default);

        conflict.HttpContext.Response.StatusCode.Should().Be(409);
        conflict.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.ConcurrencyConflict);
    }

    // CR5-3: an unknown, inactive or placeholder identity gets 401 with UNAUTHENTICATED on these three endpoints too.
    [Fact]
    public async Task CreateSpace_RefusedForAGhost_Is401WithItsCode()
    {
        var endpoint = Factory.Create<CreateSpaceEndpoint>(
            new CannedMediator(Result<CreateSpaceResult>.Failure(Error.Unauthenticated())));

        await endpoint.HandleAsync(new CreateSpaceRequest { Name = "Ghost" }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(401);
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.Unauthenticated);
    }

    [Fact]
    public async Task JoinSpace_RefusedForAGhost_Is401WithItsCode()
    {
        var endpoint = Factory.Create<JoinSpaceEndpoint>(
            new CannedMediator(Result<JoinSpaceResult>.Failure(Error.Unauthenticated())));

        await endpoint.HandleAsync(new JoinSpaceRequest { InviteCode = "KAVAXY" }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(401);
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.Unauthenticated);
    }

    [Fact]
    public async Task ConfirmPayment_RefusedForAGhost_Is401WithItsCode()
    {
        var ghost = Factory.Create<ConfirmPaymentEndpoint>(new CannedMediator(Result.Failure(Error.Unauthenticated())));

        await ghost.HandleAsync(new ConfirmPaymentRequest { SettlementId = Guid.NewGuid(), MemberUserId = Guid.NewGuid() }, default);

        ghost.HttpContext.Response.StatusCode.Should().Be(401);
        ghost.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.Unauthenticated);
    }

    // Evidence audit §4: the generate and confirm endpoints keep their 400, now with the ledger code in the body.
    [Fact]
    public async Task GenerateSettlement_OutOfOrder_Is400WithTheCode()
    {
        var endpoint = Factory.Create<GenerateSettlementEndpoint>(
            new CannedMediator(Result<BeanShare.Application.Features.Settlement.Dtos.SettlementDto>.Failure(Error.SettlementOrder("Settle or delete 'L2' first"))));

        var body = CaptureBody(endpoint);
        await endpoint.HandleAsync(new GenerateSettlementRequest { BillingPeriodId = Guid.NewGuid() }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(400);
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.SettlementOrder);
        ReadProblem(endpoint, body).Should().BeEquivalentTo(("Settle or delete 'L2' first", new[] { Error.Codes.SettlementOrder }));
        endpoint.HttpContext.Response.Headers.WWWAuthenticate.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmPayment_OfACarriedLine_Is400WithTheCode()
    {
        var endpoint = Factory.Create<ConfirmPaymentEndpoint>(
            new CannedMediator(Result.Failure(new Error(Error.Codes.LineCarried, "This line was carried; confirm it there"))));

        await endpoint.HandleAsync(new ConfirmPaymentRequest { SettlementId = Guid.NewGuid(), MemberUserId = Guid.NewGuid() }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(400);
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.LineCarried);
    }

    // CR8-1: every command endpoint answers an unknown, inactive or placeholder caller with 401 and the code, an
    // access refusal with 403, and a CONCURRENCY_CONFLICT with 409; every other failure keeps the endpoint's shape.
    [Fact]
    public async Task CloseBillingPeriod_RefusedForAGhost_Is401WithItsCode()
    {
        var endpoint = Factory.Create<CloseBillingPeriodEndpoint>(new CannedMediator(Result.Failure(Error.Unauthenticated())));

        var body = CaptureBody(endpoint);
        await endpoint.HandleAsync(new CloseBillingPeriodRequest { BillingPeriodId = Guid.NewGuid() }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(401);
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.Unauthenticated);
        endpoint.HttpContext.Response.Headers.WWWAuthenticate.ToString().Should().Be("Bearer");
        ReadProblem(endpoint, body).Should().BeEquivalentTo((Error.Unauthenticated().Message, new[] { Error.Codes.Unauthenticated }));
    }

    [Fact]
    public async Task CloseBillingPeriod_RefusedForANonAdmin_Is403WithItsCode()
    {
        var endpoint = Factory.Create<CloseBillingPeriodEndpoint>(
            new CannedMediator(Result.Failure(Error.InsufficientSpacePrivileges("close billing periods"))));

        await endpoint.HandleAsync(new CloseBillingPeriodRequest { BillingPeriodId = Guid.NewGuid() }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(403);
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.InsufficientPrivileges);
        endpoint.HttpContext.Response.Headers.WWWAuthenticate.Should().BeEmpty();
    }

    [Fact]
    public async Task ASelfShapingEndpoint_RefusedForAGhost_Is401WithItsCode()
    {
        var endpoint = Factory.Create<BeanShare.Api.Endpoints.Users.SetRevolutTagEndpoint>(new CannedMediator(Result.Failure(Error.Unauthenticated())));

        await endpoint.HandleAsync(new BeanShare.Contracts.Users.SetRevolutTagRequest(), default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(401);
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.Unauthenticated);
    }

    [Fact]
    public async Task SendingSettlementEmails_RefusedForAGhost_Is401WithItsCode()
    {
        var endpoint = Factory.Create<SendSettlementEmailsEndpoint>(new CannedMediator(Result<int>.Failure(Error.Unauthenticated())));

        await endpoint.HandleAsync(new SendSettlementEmailsRequest { Id = Guid.NewGuid() }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task ABusinessConflict_KeepsTheEndpointsOwnShape()
    {
        // ALREADY_MEMBER maps to 409 in the shared mapping, but it is neither an access failure nor a concurrency
        // conflict, so JoinSpace keeps its own 400 with the code.
        var endpoint = Factory.Create<JoinSpaceEndpoint>(
            new CannedMediator(Result<JoinSpaceResult>.Failure(new Error(Error.Codes.AlreadyMember, "Already a member"))));

        await endpoint.HandleAsync(new JoinSpaceRequest { InviteCode = "KAVAXY" }, default);

        endpoint.HttpContext.Response.StatusCode.Should().Be(400);
        endpoint.ValidationFailures.Should().ContainSingle().Which.ErrorMessage.Should().Be(Error.Codes.AlreadyMember);
    }

    /// <summary>Answers every Send with one canned response.</summary>
    // A8-1 / A8-4: a coded error body keeps the readable message in Detail next to the code in Errors[].Reason, and a
    // 401 carries the Bearer challenge. Factory.Create's context has no body stream, so give it one first.
    private static MemoryStream CaptureBody(BaseEndpoint endpoint)
    {
        var body = new MemoryStream();
        endpoint.HttpContext.Response.Body = body;
        return body;
    }

    private static (string? Detail, string[] Reasons) ReadProblem(BaseEndpoint endpoint, MemoryStream body)
    {
        endpoint.HttpContext.Response.ContentType.Should().StartWith("application/problem+json");
        using var json = JsonDocument.Parse(body.ToArray());
        // The API host serializes PascalCase; Factory.Create uses FastEndpoints' default options.
        JsonElement? Find(JsonElement e, string name) =>
            e.EnumerateObject().Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .Select(p => (JsonElement?)p.Value).FirstOrDefault();
        var detail = Find(json.RootElement, "Detail")?.GetString();
        var reasons = Find(json.RootElement, "Errors")!.Value.EnumerateArray()
            .Select(e => Find(e, "Reason")!.Value.GetString()!).ToArray();
        return (detail, reasons);
    }

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
