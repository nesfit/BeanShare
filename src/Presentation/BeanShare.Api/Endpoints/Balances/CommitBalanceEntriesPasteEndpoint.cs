using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Balances.Commands.CommitBalanceEntriesPaste;
using BeanShare.Application.Features.Balances.Dtos;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Balances;

public sealed class CommitBalanceEntriesPasteEndpoint : Endpoint<BalanceEntriesPasteRequest, IReadOnlyList<BalanceEntryDto>>
{
    private readonly IMediator _mediator;

    public CommitBalanceEntriesPasteEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{SpaceId}/balance-entries/paste");
        Summary(s =>
        {
            s.Summary = "Save a pasted batch of balance entries";
            s.Description = "Saves every row, or none when any row is invalid. Admin only.";
        });
    }

    public override async Task HandleAsync(BalanceEntriesPasteRequest req, CancellationToken ct)
    {
        if (!BalanceEntryKinds.TryParse(req.Kind, out var kind))
        {
            AddError(r => r.Kind!, "Kind must be Opening or Correction");
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var result = await _mediator.Send(new CommitBalanceEntriesPasteCommand(
            req.SpaceId, req.Text, req.BatchNote, req.CounterpartyUserId, req.EffectiveDate, kind), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendAsync(result.Value, StatusCodes.Status201Created, ct);
    }
}
