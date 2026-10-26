using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Users.Commands.UpdatePreferredCurrency;
using BeanShare.Contracts.Users;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Users;

public sealed class UpdatePreferredCurrencyEndpoint : Endpoint<UpdatePreferredCurrencyRequest>
{
    private readonly IMediator _mediator;

    public UpdatePreferredCurrencyEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/me/preferred-currency");
        Summary(s =>
        {
            s.Summary = "Update preferred currency";
            s.Description = "Update the current user's preferred currency for cost conversion. Pass null to clear.";
        });
    }

    public override async Task HandleAsync(UpdatePreferredCurrencyRequest req, CancellationToken ct)
    {
        var command = new UpdatePreferredCurrencyCommand(req.CurrencyCode);
        var result = await _mediator.Send(command, ct);

        if (result.IsFailure)
        {
            if (await this.TrySendAccessOrConflictAsync(result.Errors, ct))
                return;
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync();
            return;
        }

        await SendNoContentAsync(ct);
    }
}
