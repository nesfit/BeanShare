using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.Balances.Dtos;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Balances;

/// <summary>Only what changes: a null value leaves that setting as it is; the Clear flags remove it.</summary>
public sealed class UpdateLedgerSettingsRequest
{
    public Guid SpaceId { get; set; }
    public Guid? TreasurerUserId { get; set; }
    public DateTime? LedgerStartDate { get; set; }
    public bool ClearTreasurer { get; set; }
    public bool ClearLedgerStartDate { get; set; }
}

public sealed class UpdateLedgerSettingsEndpoint : Endpoint<UpdateLedgerSettingsRequest, LedgerSettingsDto>
{
    private readonly IMediator _mediator;

    public UpdateLedgerSettingsEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/spaces/{SpaceId}/ledger-settings");
        Summary(s =>
        {
            s.Summary = "Change the treasurer and/or the ledger start date";
            s.Description = "Null fields stay unchanged; ClearTreasurer / ClearLedgerStartDate remove a setting. Admin only.";
        });
    }

    public override async Task HandleAsync(UpdateLedgerSettingsRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateLedgerSettingsCommand(
            req.SpaceId, req.TreasurerUserId, req.LedgerStartDate, req.ClearTreasurer, req.ClearLedgerStartDate), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
