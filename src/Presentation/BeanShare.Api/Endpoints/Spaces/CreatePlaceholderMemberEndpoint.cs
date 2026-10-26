using BeanShare.Api.Endpoints.Common;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Features.Spaces.Dtos;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class CreatePlaceholderMemberRequest
{
    public Guid SpaceId { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
}

public sealed class CreatePlaceholderMemberEndpoint : Endpoint<CreatePlaceholderMemberRequest, MembershipDto>
{
    private readonly IMediator _mediator;

    public CreatePlaceholderMemberEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Post("/api/spaces/{SpaceId}/placeholders");
        Summary(s =>
        {
            s.Summary = "Add a placeholder member";
            s.Description = "A member without an account: a name and an optional email for a later link. Admin only.";
        });
    }

    public override async Task HandleAsync(CreatePlaceholderMemberRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreatePlaceholderMemberCommand(req.SpaceId, req.Name, req.Email), ct);

        if (result.IsFailure)
        {
            await this.SendResultErrorsAsync(result, ct);
            return;
        }

        await SendAsync(result.Value, StatusCodes.Status201Created, ct);
    }
}
