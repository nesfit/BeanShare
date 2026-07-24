using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

/// <summary>Marks a specific bag (lot) as the one currently being poured from.</summary>
public sealed record OpenBagCommand(Guid SpaceId, Guid LotId) : ICommand<Result<bool>>;
