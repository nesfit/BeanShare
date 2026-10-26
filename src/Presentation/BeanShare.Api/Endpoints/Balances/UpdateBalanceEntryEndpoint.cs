using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateBalanceEntry;
using BeanShare.Application.Features.Balances.Dtos;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Balances;

public sealed class UpdateBalanceEntryRequest
{
    public Guid SpaceId { get; set; }
    public Guid EntryId { get; set; }
    public Guid UserId { get; set; }
    public Guid CounterpartyUserId { get; set; }
    public decimal Amount { get; set; }
    public DateTime EffectiveDate { get; set; }
    public string Note { get; set; } = "";
    public string? Kind { get; set; }
}

public sealed class UpdateBalanceEntryEndpoint : Endpoint<UpdateBalanceEntryRequest, BalanceEntryDto>
{
    private readonly IMediator _mediator;

    public UpdateBalanceEntryEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/spaces/{SpaceId}/balance-entries/{EntryId}");
        Summary(s =>
        {
            s.Summary = "Edit a balance entry";
            s.Description = "Only while no settlement has included it (otherwise ENTRY_LOCKED). Admin only.";
        });
    }

    public override async Task HandleAsync(UpdateBalanceEntryRequest req, CancellationToken ct)
    {
        if (!BalanceEntryKinds.TryParse(req.Kind, out var kind))
        {
            AddError(r => r.Kind!, "Kind must be Opening or Correction");
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var result = await _mediator.Send(new UpdateBalanceEntryCommand(
            req.SpaceId, req.EntryId, req.UserId, req.CounterpartyUserId, req.Amount, req.EffectiveDate, req.Note, kind), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
