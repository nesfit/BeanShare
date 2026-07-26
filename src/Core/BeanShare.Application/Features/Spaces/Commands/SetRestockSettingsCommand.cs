using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;

namespace BeanShare.Application.Features.Spaces.Commands;

public sealed record SetRestockSettingsCommand(Guid SpaceId, int LeadTimeDays, decimal MinOrderValue)
    : ICommand<Result>;
