using BeanShare.Application.Features.Spaces.Queries;
using BeanShare.Contracts.Spaces;
using BeanShare.Domain.ValueObjects;
using FastEndpoints;
using MediatR;

namespace BeanShare.Api.Endpoints.Spaces;

public sealed class GetSpaceEndpoint : EndpointWithoutRequest<GetSpaceByIdResponse>
{
    private readonly IMediator _mediator;

    public GetSpaceEndpoint(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override void Configure()
    {
        Get("/api/spaces/{id}");
        Summary(s =>
        {
            s.Summary = "Get coffee space details";
            s.Description = "Retrieve detailed information about a specific coffee space";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var spaceIdRaw = Route<string>("id");
        
        if (!Guid.TryParse(spaceIdRaw, out var spaceGuid))
        {
            AddError("id", "Invalid space ID format");
            await SendErrorsAsync();
            return;
        }

        var spaceId = new SpaceId(spaceGuid);
        var query = new GetSpaceByIdQuery(spaceId);
        var result = await _mediator.Send(query, ct);

        if (result.IsFailure)
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Code, error.Message);
            }
            await SendErrorsAsync();
            return;
        }

        var response = new GetSpaceByIdResponse
        {
            Id = result.Value.Id,
            Name = result.Value.Name,
            InviteCode = result.Value.InviteCode,
            CreatedAt = result.Value.CreatedAt,
            MemberCount = result.Value.MemberCount,
            Members = result.Value.Members.Select(m => new SpaceMember
            {
                UserId = m.UserId,
                Email = m.Email,
                Role = m.Role,
                JoinedAt = m.JoinedAt,
                UserName = m.UserName,
                IsPlaceholder = m.IsPlaceholder
            }).ToList()
        };

        await SendOkAsync(response, ct);
    }
}