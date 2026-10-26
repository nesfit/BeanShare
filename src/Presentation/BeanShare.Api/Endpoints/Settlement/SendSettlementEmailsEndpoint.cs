using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Settlement.Commands.SendSettlementEmails;
using BeanShare.Domain.ValueObjects;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Settlement;

public sealed class SendSettlementEmailsRequest
{
    public Guid Id { get; init; }
    public bool AttachPdf { get; init; } = true;
}

public sealed class SendSettlementEmailsResponse
{
    public int EmailsSent { get; init; }
    public string Message { get; init; } = "";
}

public sealed class SendSettlementEmailsEndpoint : Endpoint<SendSettlementEmailsRequest, SendSettlementEmailsResponse>
{
    private readonly IMediator _mediator;

    public SendSettlementEmailsEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/settlements/{Id}/send-emails");
        Summary(s =>
        {
            s.Summary = "Send settlement emails";
            s.Description = "Sends settlement notification emails to the members with something to read or do (consumption, a non-zero net, or the treasurer when payments exist). Space admins only.";
        });
    }

    public override async Task HandleAsync(SendSettlementEmailsRequest req, CancellationToken ct)
    {
        var command = new SendSettlementEmailsCommand(new SettlementId(req.Id), req.AttachPdf);
        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
        {
            await SendOkAsync(new SendSettlementEmailsResponse
            {
                EmailsSent = result.Value,
                Message = $"Successfully sent {result.Value} email(s)"
            }, ct);
        }
        else if (!await this.TrySendAccessOrConflictAsync(result.Errors, ct))
        {
            await SendResultAsync(Results.BadRequest(result.Errors.Any() ? result.Errors.First().Message : "Failed to send emails"));
        }
    }
}
