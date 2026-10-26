using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Consumption.Commands;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Consumption;

public sealed class DeleteConsumptionRequest
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
}

public sealed class DeleteConsumptionEndpoint(IMediator mediator)
    : Endpoint<DeleteConsumptionRequest>
{
    public override void Configure()
    {
        Delete("/api/consumptions/{Id}");
        Summary(s =>
        {
            s.Summary = "Delete a consumption entry";
            s.Description = "Deletes a consumption entry. Only allowed for entries in open or draft billing periods.";
        });
    }

    public override async Task HandleAsync(DeleteConsumptionRequest req, CancellationToken ct)
    {
        var command = new DeleteConsumptionCommand(req.Id, req.SpaceId);
        var result = await mediator.Send(command, ct);

        if (await this.TrySendConflictAsync(result.Errors, ct))
            return;

        if (result.IsSuccess)
        {
            await SendNoContentAsync(ct);
        }
        else
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync(cancellation: ct);
        }
    }
}
