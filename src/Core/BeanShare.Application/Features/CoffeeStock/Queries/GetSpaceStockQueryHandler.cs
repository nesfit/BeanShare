using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.CoffeeStock.Dtos;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MapsterMapper;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Queries;
public sealed class GetSpaceStockQueryHandler : IRequestHandler<GetSpaceStockQuery, Result<CoffeeStockDto>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserContext _userContext;
    private readonly IMapper _mapper;
    private readonly IClock _clock;

    public GetSpaceStockQueryHandler(
        ICoffeeStockRepository coffeeStockRepository,
        ISpaceRepository spaceRepository,
        IUserRepository userRepository,
        IUserContext userContext,
        IMapper mapper,
        IClock clock)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _spaceRepository = spaceRepository;
        _userRepository = userRepository;
        _userContext = userContext;
        _mapper = mapper;
        _clock = clock;
    }

    public async Task<Result<CoffeeStockDto>> Handle(GetSpaceStockQuery query, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(query.SpaceId);
        var spaceSpec = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);

        if (space == null)
        {
            return Result<CoffeeStockDto>.Failure(Error.SpaceNotFound(query.SpaceId));
        }

        var currentUserId = _userContext.CurrentUserId;
        if (!space.HasMember(currentUserId))
        {
            return Result<CoffeeStockDto>.Failure(Error.InsufficientSpacePrivileges("view coffee stock"));
        }

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);

        if (coffeeStock == null)
        {
            return Result<CoffeeStockDto>.Success(new CoffeeStockDto
            {
                Id = Guid.Empty,
                SpaceId = query.SpaceId,
                CreatedAt = _clock.UtcNow,
                UpdatedAt = _clock.UtcNow,
                PurchaseCount = 0,
                ProductVarietyCount = 0,
                TotalCurrentStockGrams = 0,
                TotalInvestmentAmount = 0,
                TotalInvestmentCurrency = space.Currency.Code,
                TotalPurchaseCount = 0,
                StockLevels = [],
                RecentPurchases = []
            });
        }

        var totalPurchaseCount = coffeeStock.Purchases.Count;

        var dto = new CoffeeStockDto
        {
            Id = coffeeStock.Id.Value,
            SpaceId = coffeeStock.SpaceId.Value,
            CreatedAt = coffeeStock.CreatedAt,
            UpdatedAt = coffeeStock.UpdatedAt,
            PurchaseCount = totalPurchaseCount,
            ProductVarietyCount = coffeeStock.ProductVarietyCount,
            TotalCurrentStockGrams = coffeeStock.TotalCurrentStock.Grams,
            TotalInvestmentAmount = coffeeStock.Purchases.Sum(p => p.Cost.Amount),
            TotalInvestmentCurrency = space.Currency.Code,
            TotalPurchaseCount = totalPurchaseCount,
            StockLevels = _mapper.Map<List<StockLevelDto>>(coffeeStock.StockLevels.Where(sl => !sl.IsArchived).ToList()),
            RecentPurchases = await BuildPurchaseDtosAsync(coffeeStock, query.Page, query.PageSize, cancellationToken)
        };

        return Result<CoffeeStockDto>.Success(dto);
    }

    private async Task<List<StockPurchaseDto>> BuildPurchaseDtosAsync(
        BeanShare.Domain.Aggregates.CoffeeStock.CoffeeStock coffeeStock,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var recentPurchases = coffeeStock.Purchases
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var distinctUserIds = recentPurchases
            .SelectMany(p => p.PaidByUserId is { } paidBy ? new[] { p.PurchasedBy, paidBy } : new[] { p.PurchasedBy })
            .Distinct()
            .ToList();

        var userNames = new Dictionary<UserId, string>();
        foreach (var userId in distinctUserIds)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user != null)
                userNames[userId] = user.Name;
        }

        return recentPurchases.Select(p => new StockPurchaseDto
        {
            Id = p.Id,
            ProductName = p.Product.Name,
            ProductBrand = p.Product.Brand,
            ProductType = p.Product.Type.ToString(),
            QuantityGrams = p.Quantity.Grams,
            CostAmount = p.Cost.Amount,
            CostCurrency = p.Cost.Currency,
            Vendor = p.Vendor,
            PurchasedBy = p.PurchasedBy.Value,
            PurchasedByName = userNames.GetValueOrDefault(p.PurchasedBy, "Unknown"),
            PaidByUserId = p.PaidByUserId?.Value,
            PaidByName = p.PaidByUserId is { } paidBy ? userNames.GetValueOrDefault(paidBy, "Unknown") : null,
            PurchasedAt = p.PurchasedAt,
            CreatedAt = p.CreatedAt,
            CostPerGram = p.Cost.Amount / p.Quantity.Grams,
            ConsumedGrams = p.ConsumedGrams,
            RemainingGrams = p.RemainingGrams.Grams,
            Status = p.Status.ToString(),
            Label = p.Label,
            IsOpen = p.IsOpen,
            OpenedAt = p.OpenedAt,
            DepletedAt = p.DepletedAt,
            WrittenOffGrams = p.WrittenOffGrams,
            DiscardedGrams = p.DiscardedGrams,
            CanUndoEmpty = p.IsWriteOffUndoable(_clock.UtcNow),
            UndoEmptyUntil = p.IsWriteOffUndoable(_clock.UtcNow) ? p.WriteOffUndoDeadline : null
        }).ToList();
    }
}