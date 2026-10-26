using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Balances.Commands.CreateBalanceEntry;
using BeanShare.Application.Features.Balances.Dtos;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Balances;

public sealed class CreateBalanceEntryRequest
{
    public Guid SpaceId { get; set; }
    public Guid UserId { get; set; }
    public Guid? CounterpartyUserId { get; set; }
    public decimal Amount { get; set; }
    public DateTime EffectiveDate { get; set; }
    public string Note { get; set; } = "";
    public string? Kind { get; set; }
}

public sealed class CreateBalanceEntryEndpoint : Endpoint<CreateBalanceEntryRequest, BalanceEntryDto>
{
    private readonly IMediator _mediator;

    public CreateBalanceEntryEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{SpaceId}/balance-entries");
        Summary(s =>
        {
            s.Summary = "Create a balance entry";
            s.Description = "Records +Amount for the member and −Amount for the counterparty (default: the caller). Admin only.";
        });
    }

    public override async Task HandleAsync(CreateBalanceEntryRequest req, CancellationToken ct)
    {
        if (!BalanceEntryKinds.TryParse(req.Kind, out var kind))
        {
            AddError(r => r.Kind!, "Kind must be Opening or Correction");
            await SendErrorsAsync(cancellation: ct);
            return;
        }

        var result = await _mediator.Send(new CreateBalanceEntryCommand(
            req.SpaceId, req.UserId, req.Amount, req.EffectiveDate, req.Note, kind, req.CounterpartyUserId), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendAsync(result.Value, StatusCodes.Status201Created, ct);
    }
}
