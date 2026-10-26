using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Common.Authorization;

namespace BeanShare.Application.Features.CoffeeStock.Commands.SetBagPaidInExcel;

/// <summary>
/// Marks a bag bought before the ledger start as "Paid in Excel" (DS-41), or clears the mark. A flagged bag
/// earns no BeanShare credit and is left out of the fallback price; its stamped cups are priced as fallback
/// cups. Refused once the space has a ledger settlement.
/// </summary>
[RequireSpaceAdmin("SpaceId")]
public sealed record SetBagPaidInExcelCommand(Guid SpaceId, Guid PurchaseId, bool PaidInExcel)
    : IAuthorize, ICommand<Result>;
