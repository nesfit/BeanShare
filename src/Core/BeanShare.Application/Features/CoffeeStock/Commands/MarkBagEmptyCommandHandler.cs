using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

public sealed class MarkBagEmptyCommandHandler : IRequestHandler<MarkBagEmptyCommand, Result<MarkBagEmptyResult>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;

    public MarkBagEmptyCommandHandler(
        ICoffeeStockRepository coffeeStockRepository,
        IConsumptionRepository consumptionRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext,
        IClock clock,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _consumptionRepository = consumptionRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<MarkBagEmptyResult>> Handle(MarkBagEmptyCommand command, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(command.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space == null)
            return Result<MarkBagEmptyResult>.Failure(Error.SpaceNotFound(command.SpaceId));

        if (!space.IsAdmin(_userContext.CurrentUserId))
            return Result<MarkBagEmptyResult>.Failure(Error.InsufficientSpacePrivileges("empty a bag"));

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock == null)
            return Result<MarkBagEmptyResult>.Failure(Error.StockNotFound(command.SpaceId));

        var bag = coffeeStock.Purchases.FirstOrDefault(p => p.Id == command.LotId);
        if (bag == null)
            return Result<MarkBagEmptyResult>.Failure(Error.StockLevelNotFound(command.LotId));

        if (bag.IsEmpty || !bag.RemainingGrams.IsPositive)
            return Result<MarkBagEmptyResult>.Failure(Error.NoRemainingStock());

        var remainder = bag.RemainingGrams.Grams;
        var product = CoffeeProduct.Create(bag.Product.Name, bag.Product.Brand, bag.Product.Type);

        // Who absorbs the remainder, in priority order: this bag's own drinkers → the product's
        // drinkers → any space consumer → an equal split among members.
        // Only current, non-merged members share a write-off, renormalised over them: ex-members and
        // merged placeholders are never billed.
        var consumptions = await WriteOffParticipants.ShareableCupsAsync(
            space, await _consumptionRepository.GetBySpaceIdAsync(spaceId, cancellationToken), _userRepository, cancellationToken);

        var byUser =
            GramsByUser(consumptions.Where(c => c.StockLotId == command.LotId));
        if (byUser.Count == 0)
            byUser = GramsByUser(consumptions.Where(c => SameProduct(c.Product, product)));
        if (byUser.Count == 0)
            byUser = GramsByUser(consumptions);

        var shares = BuildShares(byUser, remainder, await WriteOffParticipants.EqualSplitMembersAsync(space, _userRepository, cancellationToken));
        if (shares.Count == 0)
            return Result<MarkBagEmptyResult>.Failure(Error.NoRemainingStock());

        var now = _clock.UtcNow;
        foreach (var (userId, grams) in shares)
        {
            var weight = Weight.FromGrams(grams);
            if (!weight.IsPositive)
                continue;

            // Write off from THIS bag so the cup is stamped at its €/g and the bag depletes to Empty;
            // the bag remembers the written-off grams so the emptying can be undone (UndoBagEmpty).
            var allocation = coffeeStock.WriteOffStock(command.LotId, weight, _clock);

            var entryProduct = CoffeeProduct.Create(product.Name, product.Brand, product.Type);
            var entry = ConsumptionEntry.Create(
                spaceId,
                userId,
                entryProduct,
                weight,
                now,
                _clock,
                presetId: null,
                presetName: command.Reason,
                stockLotId: allocation.LotId,
                cost: allocation.Cost,
                unitCostPerGram: allocation.UnitCostPerGram,
                isWriteOff: true);

            await _consumptionRepository.AddAsync(entry, cancellationToken);
        }

        await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);
        // Changes members' balances, so it shares the space's ledger token with Leave, Remove and generation.
        space.BumpLedgerVersion();
        await _spaceRepository.UpdateAsync(space, cancellationToken);

        return Result<MarkBagEmptyResult>.Success(new MarkBagEmptyResult(
            command.LotId,
            product.DisplayName,
            bag.Label,
            shares.Sum(s => s.Grams),
            shares.Count));
    }

    private static Dictionary<UserId, decimal> GramsByUser(IEnumerable<ConsumptionEntry> entries) =>
        entries.GroupBy(c => c.UserId).ToDictionary(g => g.Key, g => g.Sum(c => c.Quantity.Grams));

    /// <summary>
    /// Split <paramref name="remainder"/> across users pro-rata to their consumed grams (or equally
    /// among members when there is no consumption history), rounded to 1 dp, with any rounding residual
    /// assigned to the largest share so the shares sum to exactly the remainder (bag depletes to zero).
    /// </summary>
    private static List<(UserId User, decimal Grams)> BuildShares(
        Dictionary<UserId, decimal> byUser, decimal remainder, IReadOnlyList<UserId> members)
    {
        var shares = new List<(UserId User, decimal Grams)>();

        if (byUser.Count > 0)
        {
            var total = byUser.Values.Sum();
            if (total <= 0)
                return shares;

            foreach (var (userId, grams) in byUser)
            {
                var share = Math.Round(remainder * (grams / total), 1);
                if (share > 0)
                    shares.Add((userId, share));
            }
        }
        else if (members.Count > 0)
        {
            var each = Math.Round(remainder / members.Count, 1);
            foreach (var member in members)
                shares.Add((member, each));
        }

        if (shares.Count == 0)
            return shares;

        var residual = remainder - shares.Sum(s => s.Grams);
        if (residual != 0)
        {
            var topIndex = 0;
            for (var i = 1; i < shares.Count; i++)
                if (shares[i].Grams > shares[topIndex].Grams)
                    topIndex = i;

            shares[topIndex] = (shares[topIndex].User, shares[topIndex].Grams + residual);
        }

        return shares;
    }

    private static bool SameProduct(CoffeeProduct a, CoffeeProduct b) =>
        string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Brand, b.Brand, StringComparison.OrdinalIgnoreCase) &&
        a.Type == b.Type;
}
