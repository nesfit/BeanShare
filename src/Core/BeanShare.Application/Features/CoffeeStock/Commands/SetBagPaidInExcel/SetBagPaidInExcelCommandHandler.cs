using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BeanShare.Application.Features.CoffeeStock.Commands.SetBagPaidInExcel;

public sealed class SetBagPaidInExcelCommandHandler : IRequestHandler<SetBagPaidInExcelCommand, Result>
{
    private readonly ISpaceRepository _spaceRepository;
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly ILogger<SetBagPaidInExcelCommandHandler> _logger;

    public SetBagPaidInExcelCommandHandler(
        ISpaceRepository spaceRepository,
        ICoffeeStockRepository coffeeStockRepository,
        ISettlementRepository settlementRepository,
        IUserContext userContext,
        IClock clock,
        ILogger<SetBagPaidInExcelCommandHandler> logger)
    {
        _spaceRepository = spaceRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _settlementRepository = settlementRepository;
        _userContext = userContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> Handle(SetBagPaidInExcelCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space is null)
            return Result.Failure(Error.SpaceNotFound(command.SpaceId));

        if (space.LedgerStartDate is not { } ledgerStart)
            return Result.Failure(Error.ValidationFailure("LedgerStartDate", "Set the ledger start date first"));

        // The flag is a cutover input, like the ledger start itself (DS-37).
        if (await _settlementRepository.HasLedgerSettlementAsync(spaceId, cancellationToken))
            return Result.Failure(Error.LedgerStartLocked());

        var stock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        var bag = stock?.Purchases.FirstOrDefault(p => p.Id == command.PurchaseId);
        if (stock is null || bag is null)
            return Result.Failure(Error.StockLevelNotFound(command.PurchaseId));

        if (command.PaidInExcel && bag.PurchasedAt >= ledgerStart)
            return Result.Failure(Error.ValidationFailure(
                "PurchaseId", $"Only a bag bought before the ledger start ({ledgerStart:yyyy-MM-dd}) can be marked Paid in Excel"));

        if (bag.ExcludedFromLedgerCredit == command.PaidInExcel)
            return Result.Success();

        stock.SetPurchaseExcludedFromLedgerCredit(bag.Id, command.PaidInExcel, _clock);
        await _coffeeStockRepository.UpdateAsync(stock, cancellationToken);

        // A ledger input changed, so a racing generation or preview-then-generate conflicts.
        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);

        _logger.LogInformation(
            "Bag {PurchaseId} in space {SpaceId} {Action} Paid in Excel by {AdminId}",
            bag.Id, spaceId, command.PaidInExcel ? "marked" : "unmarked", _userContext.CurrentUserId);
        return Result.Success();
    }
}
