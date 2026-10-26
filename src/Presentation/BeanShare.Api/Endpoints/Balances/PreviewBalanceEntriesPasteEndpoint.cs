using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Balances.Dtos;
using BeanShare.Application.Features.Balances.Queries.PreviewBalanceEntriesPaste;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Balances;

/// <summary>A pasted <c>member;amount;note</c> batch, one row per line.</summary>
public sealed class BalanceEntriesPasteRequest
{
    public Guid SpaceId { get; set; }
    public string Text { get; set; } = "";
    public string? BatchNote { get; set; }
    public Guid? CounterpartyUserId { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public string? Kind { get; set; }
}

public sealed class PreviewBalanceEntriesPasteEndpoint : Endpoint<BalanceEntriesPasteRequest, BalancePastePreviewDto>
{
    private readonly IMediator _mediator;

    public PreviewBalanceEntriesPasteEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{SpaceId}/balance-entries/paste/preview");
        Summary(s =>
        {
            s.Summary = "Preview a pasted batch of balance entries";
            s.Description = "Matches members, reports errors and the sum; saves nothing. Admin only.";
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

        var result = await _mediator.Send(new PreviewBalanceEntriesPasteQuery(
            req.SpaceId, req.Text, req.BatchNote, req.CounterpartyUserId, req.EffectiveDate, kind), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
