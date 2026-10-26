using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Features.Spaces.Dtos;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class UpdatePlaceholderMemberRequest
{
    public Guid SpaceId { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
}

public sealed class UpdatePlaceholderMemberEndpoint : Endpoint<UpdatePlaceholderMemberRequest, MembershipDto>
{
    private readonly IMediator _mediator;

    public UpdatePlaceholderMemberEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Put("/api/spaces/{SpaceId}/placeholders/{UserId}");
        Summary(s =>
        {
            s.Summary = "Rename a placeholder or change its email";
            s.Description = "A merged placeholder is refused (MEMBER_MERGED). Admin only.";
        });
    }

    public override async Task HandleAsync(UpdatePlaceholderMemberRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdatePlaceholderMemberCommand(req.SpaceId, req.UserId, req.Name, req.Email), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
