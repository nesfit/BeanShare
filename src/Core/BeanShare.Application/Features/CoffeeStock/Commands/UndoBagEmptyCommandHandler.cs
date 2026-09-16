using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

public sealed class UndoBagEmptyCommandHandler : IRequestHandler<UndoBagEmptyCommand, Result<UndoBagEmptyResult>>
{
    /// <summary>
    /// The phantom-fill cups are created in the same request that depletes the bag, so their
    /// CreatedAt is within milliseconds of DepletedAt — on either side of it. This slack only has to
    /// cover that skew, not human time.
    /// </summary>
    private static readonly TimeSpan CreationSkew = TimeSpan.FromMinutes(1);

    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public UndoBagEmptyCommandHandler(
        ICoffeeStockRepository coffeeStockRepository,
        IConsumptionRepository consumptionRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext,
        IClock clock)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _consumptionRepository = consumptionRepository;
        _billingPeriodRepository = billingPeriodRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<UndoBagEmptyResult>> Handle(UndoBagEmptyCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space == null)
            return Result<UndoBagEmptyResult>.Failure(Error.SpaceNotFound(command.SpaceId));

        if (!space.IsAdmin(_userContext.CurrentUserId))
            return Result<UndoBagEmptyResult>.Failure(Error.InsufficientSpacePrivileges("undo emptying a bag"));

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock == null)
            return Result<UndoBagEmptyResult>.Failure(Error.StockNotFound(command.SpaceId));

        var bag = coffeeStock.Purchases.FirstOrDefault(p => p.Id == command.LotId);
        if (bag == null)
            return Result<UndoBagEmptyResult>.Failure(Error.StockLevelNotFound(command.LotId));

        if (!bag.IsEmpty || bag.EmptiedByAdminGrams <= 0 || bag.DepletedAt is null)
            return Result<UndoBagEmptyResult>.Failure(
                Error.CannotUndoBagEmpty("it was not emptied by an admin (it ran out cup by cup, or the emptying was already undone)"));

        var now = _clock.UtcNow;
        if (!bag.IsWriteOffUndoable(now))
            return Result<UndoBagEmptyResult>.Failure(
                Error.CannotUndoBagEmpty($"the {Purchase.WriteOffUndoWindow.TotalHours:0} h undo window closed at {bag.WriteOffUndoDeadline:u}"));

        // A discard (unbilled) has no cups to remove; a write-off (billed) must still have all of them.
        var entries = bag.WrittenOffGrams > 0
            ? await _consumptionRepository.GetBySpecAsync(
                new WriteOffEntriesByLotSpecification(bag.Id, bag.DepletedAt.Value - CreationSkew), cancellationToken)
            : [];

        if (bag.WrittenOffGrams > 0 && entries.Count == 0)
            return Result<UndoBagEmptyResult>.Failure(
                Error.CannotUndoBagEmpty("the billed cups are no longer there (already deleted by hand?)"));

        // The billed cups and the bag's write-off must still describe the same emptying.
        var billedGrams = entries.Sum(e => e.Quantity.Grams);
        if (Math.Abs(billedGrams - bag.WrittenOffGrams) > 0.1m)
            return Result<UndoBagEmptyResult>.Failure(
                Error.CannotUndoBagEmpty($"the billed cups ({billedGrams:0.#} g) no longer match the write-off ({bag.WrittenOffGrams:0.#} g); delete the remaining cups by hand"));

        // Same rule as deleting a cup: nothing leaves a closed or settled period.
        foreach (var periodId in entries.Where(e => e.BillingPeriodId != null).Select(e => e.BillingPeriodId!.Value).Distinct())
        {
            var period = await _billingPeriodRepository.GetByIdAsync(periodId, cancellationToken);
            if (period != null && period.State != BillingState.Draft && period.State != BillingState.Open)
                return Result<UndoBagEmptyResult>.Failure(
                    Error.CannotUndoBagEmpty($"its cups are already in a {period.State.ToString().ToLowerInvariant()} billing period"));
        }

        Weight restored;
        try
        {
            restored = coffeeStock.UndoBagWriteOff(bag.Id, _clock);
        }
        catch (DomainException ex)
        {
            return Result<UndoBagEmptyResult>.Failure(Error.DomainError(ex.Message));
        }

        foreach (var entry in entries)
            await _consumptionRepository.RemoveAsync(entry, cancellationToken);

        await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);

        return Result<UndoBagEmptyResult>.Success(new UndoBagEmptyResult(
            bag.Id,
            bag.Product.DisplayName,
            bag.Label,
            restored.Grams,
            entries.Count,
            bag.Status.ToString()));
    }
}
