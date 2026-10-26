using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Services;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Commands;
public sealed class SplitRemainingStockCommandHandler : IRequestHandler<SplitRemainingStockCommand, Result<SplitRemainingStockResult>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserService _userService;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;

    public SplitRemainingStockCommandHandler(
        ICoffeeStockRepository coffeeStockRepository,
        IConsumptionRepository consumptionRepository,
        ISpaceRepository spaceRepository,
        IUserService userService,
        IUserContext userContext,
        IClock clock)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _consumptionRepository = consumptionRepository;
        _spaceRepository = spaceRepository;
        _userService = userService;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<SplitRemainingStockResult>> Handle(SplitRemainingStockCommand request, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(request.SpaceId);

        var spaceSpec = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);
        if (space == null)
        {
            return Result<SplitRemainingStockResult>.Failure(Error.SpaceNotFound(request.SpaceId));
        }

        if (!space.IsAdmin(_userContext.CurrentUserId))
        {
            return Result<SplitRemainingStockResult>.Failure(Error.InsufficientSpacePrivileges("split remaining stock"));
        }

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock == null)
        {
            return Result<SplitRemainingStockResult>.Failure(Error.StockNotFound(request.SpaceId));
        }

        var stockLevel = coffeeStock.StockLevels.FirstOrDefault(sl => sl.Id == request.StockLevelId);
        if (stockLevel == null)
        {
            return Result<SplitRemainingStockResult>.Failure(Error.StockLevelNotFound(request.StockLevelId));
        }

        var remainingGrams = stockLevel.CurrentStock.Grams;
        if (remainingGrams <= 0)
        {
            return Result<SplitRemainingStockResult>.Failure(Error.NoRemainingStock());
        }

        var product = CoffeeProduct.Create(stockLevel.Product.Name, stockLevel.Product.Brand, stockLevel.Product.Type);

        var allConsumptions = await _consumptionRepository.GetBySpaceIdAsync(spaceId, cancellationToken);

        // Only current members share a write-off, renormalised over them (DS-34).
        var productConsumptions = allConsumptions
            .Where(c => c.Product.Name == product.Name && c.Product.Brand == product.Brand && c.Product.Type == product.Type)
            .Where(c => space.HasMember(c.UserId))
            .ToList();

        var memberIds = space.Members.Select(m => m.UserId).ToList();
        var users = await _userService.GetByIdsAsync(memberIds, cancellationToken);
        var userLookup = users.ToDictionary(u => u.Id, u => u.Name);

        var allocations = new List<MemberAllocationResult>();
        var consumptionByUser = productConsumptions
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Quantity.Grams));

        var totalConsumedByAllUsers = consumptionByUser.Values.Sum();

        if (totalConsumedByAllUsers > 0)
        {
            foreach (var (userId, userConsumedGrams) in consumptionByUser)
            {
                var percentage = (userConsumedGrams / totalConsumedByAllUsers) * 100;
                var allocatedGrams = Math.Round((userConsumedGrams / totalConsumedByAllUsers) * remainingGrams, 2);

                if (allocatedGrams > 0)
                {
                    allocations.Add(new MemberAllocationResult(
                        userId.Value,
                        userLookup.GetValueOrDefault(userId, "Unknown User"),
                        Math.Round(percentage, 2),
                        allocatedGrams));
                }
            }
        }
        else if (memberIds.Count > 0)
        {
            var memberCount = memberIds.Count;
            var equalShare = Math.Round(remainingGrams / memberCount, 2);
            var equalPercentage = Math.Round(100.0m / memberCount, 2);

            foreach (var memberId in memberIds)
            {
                allocations.Add(new MemberAllocationResult(
                    memberId.Value,
                    userLookup.GetValueOrDefault(memberId, "Unknown User"),
                    equalPercentage,
                    equalShare));
            }
        }

        var now = _clock.UtcNow;

        // Each member's share is drawn bag by bag, oldest first (cups never span bags), and every draw
        // becomes its own entry stamped with that bag's cost, so the bag's payer is credited (B4).
        var nonEmptyBags = coffeeStock.Purchases
            .Where(p => !p.IsEmpty
                && p.Product.Name == product.Name
                && p.Product.Brand == product.Brand
                && p.Product.Type == product.Type)
            .OrderBy(p => p.PurchasedAt).ThenBy(p => p.CreatedAt)
            .ToList();

        var totalDistributed = 0m;
        foreach (var allocation in allocations.Where(a => a.AllocatedGrams > 0))
        {
            var remainingShare = allocation.AllocatedGrams;
            foreach (var bag in nonEmptyBags)
            {
                if (remainingShare <= 0)
                    break;

                var take = Math.Min(remainingShare, bag.RemainingGrams.Grams);
                if (take <= 0)
                    continue;

                var draw = coffeeStock.ConsumeStock(product, Weight.FromGrams(take), _clock, bag.Id);
                remainingShare -= take;
                totalDistributed += take;

                // Each ConsumptionEntry needs its own CoffeeProduct instance for EF owned-type tracking
                var entryProduct = CoffeeProduct.Create(product.Name, product.Brand, product.Type);
                var consumptionEntry = ConsumptionEntry.Create(
                    spaceId,
                    new UserId(allocation.UserId),
                    entryProduct,
                    Weight.FromGrams(take),
                    now,
                    _clock,
                    presetId: null,
                    presetName: request.Reason,
                    stockLotId: draw.LotId,
                    cost: draw.Cost,
                    unitCostPerGram: draw.UnitCostPerGram,
                    isWriteOff: true);

                await _consumptionRepository.AddAsync(consumptionEntry, cancellationToken);
            }
        }

        await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);

        return Result<SplitRemainingStockResult>.Success(new SplitRemainingStockResult(
            request.SpaceId,
            request.StockLevelId,
            product.Name,
            product.Brand,
            product.Type.ToString(),
            totalDistributed,
            allocations));
    }
}
