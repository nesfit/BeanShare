using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Spaces.Commands;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class MergePlaceholderRequest
{
    public Guid SpaceId { get; set; }
    public Guid UserId { get; set; }
    public Guid TargetUserId { get; set; }
}

public sealed class MergePlaceholderEndpoint : Endpoint<MergePlaceholderRequest, EmptyResponse>
{
    private readonly IMediator _mediator;

    public MergePlaceholderEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{SpaceId}/placeholders/{UserId}/merge");
        Summary(s =>
        {
            s.Summary = "Merge a placeholder into a member";
            s.Description = "The placeholder's balance moves to the target at every later calculation; it is then hidden. Admin only.";
        });
    }

    public override async Task HandleAsync(MergePlaceholderRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new MergePlaceholderCommand(req.SpaceId, req.UserId, req.TargetUserId), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }
}
