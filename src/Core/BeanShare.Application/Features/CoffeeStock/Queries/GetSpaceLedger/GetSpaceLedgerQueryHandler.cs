using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Queries.GetSpaceLedger;

public sealed class GetSpaceLedgerQueryHandler : IRequestHandler<GetSpaceLedgerQuery, Result<LedgerPageDto>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserContext _userContext;

    public GetSpaceLedgerQueryHandler(
        ICoffeeStockRepository coffeeStockRepository,
        IConsumptionRepository consumptionRepository,
        ISpaceRepository spaceRepository,
        IUserRepository userRepository,
        IUserContext userContext)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _consumptionRepository = consumptionRepository;
        _spaceRepository = spaceRepository;
        _userRepository = userRepository;
        _userContext = userContext;
    }

    public async Task<Result<LedgerPageDto>> Handle(GetSpaceLedgerQuery query, CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(query.SpaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(new SpaceByIdSpecification(spaceId), cancellationToken);
        if (space == null)
            return Result<LedgerPageDto>.Failure(Error.SpaceNotFound(query.SpaceId));

        if (!space.HasMember(_userContext.CurrentUserId))
            return Result<LedgerPageDto>.Failure(Error.InsufficientSpacePrivileges("view the ledger"));

        var currency = space.Currency.Code;
        var currentUser = _userContext.CurrentUserId;
        var isAdmin = space.IsAdmin(currentUser);

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        var purchases = coffeeStock?.Purchases.ToList() ?? [];
        var cups = (await _consumptionRepository.GetBySpaceIdAsync(spaceId, cancellationToken)).ToList();

        var labelByLot = purchases.ToDictionary(p => p.Id, p => p.Label);

        var movements = new List<Movement>(purchases.Count + cups.Count);
        foreach (var p in purchases)
        {
            movements.Add(new Movement
            {
                Timestamp = p.PurchasedAt,
                Kind = "Purchase",
                Product = p.Product.DisplayName,
                ProductType = p.Product.Type.ToString(),
                Bag = string.IsNullOrEmpty(p.Label) ? "—" : p.Label,
                Delta = p.Quantity.Grams,
                UserId = p.PurchasedBy.Value,
                CostAmount = p.Cost.Amount,
                CostCurrency = p.Cost.Currency.Code,
                Note = p.Vendor,
                Key = ProductKey(p.Product),
                ProductName = p.Product.Name,
                ProductBrand = p.Product.Brand,
            });
        }

        // Coffee an admin threw away unbilled: an outflow nobody drank, so the product balance still
        // reaches zero and the sunk cost is visible. Attributed to nobody (UserId empty → "—").
        foreach (var p in purchases.Where(p => p.DiscardedGrams > 0))
        {
            movements.Add(new Movement
            {
                Timestamp = p.DepletedAt ?? p.CreatedAt,
                Kind = "Discard",
                Product = p.Product.DisplayName,
                ProductType = p.Product.Type.ToString(),
                Bag = string.IsNullOrEmpty(p.Label) ? "—" : p.Label,
                Delta = -p.DiscardedGrams,
                UserId = Guid.Empty,
                CostAmount = Math.Round(p.UnitCostPerGram * p.DiscardedGrams, 2),
                CostCurrency = p.Cost.Currency.Code,
                Note = "Discarded — not billed",
                Key = ProductKey(p.Product),
                ProductName = p.Product.Name,
                ProductBrand = p.Product.Brand,
            });
        }

        foreach (var c in cups)
        {
            var bag = c.StockLotId.HasValue && labelByLot.TryGetValue(c.StockLotId.Value, out var lbl) && !string.IsNullOrEmpty(lbl)
                ? lbl
                : "—";
            movements.Add(new Movement
            {
                Timestamp = c.ConsumedAt,
                Kind = "Cup",
                Product = c.Product.DisplayName,
                ProductType = c.Product.Type.ToString(),
                Bag = bag,
                Delta = -c.Quantity.Grams,
                UserId = c.UserId.Value,
                CostAmount = c.Cost?.Amount,
                CostCurrency = c.Cost?.Currency.Code,
                Note = c.PresetName,
                Key = ProductKey(c.Product),
                EntryId = c.Id.Value,
                CanEdit = c.UserId == currentUser || isAdmin,
                ProductName = c.Product.Name,
                ProductBrand = c.Product.Brand,
            });
        }

        // Per-product running balance over the full chronological set (purchases before cups on a tie),
        // computed before filtering so the balance is always the product's true running total.
        var chronological = movements
            .OrderBy(m => m.Timestamp)
            .ThenBy(m => m.Kind == "Purchase" ? 0 : 1)
            .ToList();

        var balances = new Dictionary<(string, string, CoffeeType), decimal>();
        foreach (var m in chronological)
        {
            var running = balances.GetValueOrDefault(m.Key, 0m) + m.Delta;
            balances[m.Key] = running;
            m.Balance = running;
        }

        // Filters
        IEnumerable<Movement> filtered = chronological;
        if (string.Equals(query.Kind, "purchase", StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(m => m.Kind == "Purchase");
        else if (string.Equals(query.Kind, "cup", StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(m => m.Kind == "Cup");
        else if (string.Equals(query.Kind, "discard", StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(m => m.Kind == "Discard");
        if (!string.IsNullOrWhiteSpace(query.Product))
            filtered = filtered.Where(m => m.Product.Contains(query.Product, StringComparison.OrdinalIgnoreCase));
        if (query.MemberUserId.HasValue)
            filtered = filtered.Where(m => m.UserId == query.MemberUserId.Value);
        if (query.StartDate.HasValue)
            filtered = filtered.Where(m => m.Timestamp >= query.StartDate.Value);
        if (query.EndDate.HasValue)
            filtered = filtered.Where(m => m.Timestamp <= query.EndDate.Value);

        var filteredList = filtered.ToList();

        var purchaseRows = filteredList.Where(m => m.Kind == "Purchase").ToList();
        var cupRows = filteredList.Where(m => m.Kind == "Cup").ToList();
        var discardRows = filteredList.Where(m => m.Kind == "Discard").ToList();
        var totals = new LedgerTotalsDto(
            PurchasedGrams: purchaseRows.Sum(m => m.Delta),
            PurchasedCost: purchaseRows.Sum(m => m.CostAmount ?? 0m),
            ConsumedGrams: cupRows.Sum(m => -m.Delta),
            ConsumedCost: cupRows.Sum(m => m.CostAmount ?? 0m),
            NetGrams: filteredList.Sum(m => m.Delta),
            PurchaseCount: purchaseRows.Count,
            CupCount: cupRows.Count,
            Currency: currency,
            DiscardedGrams: discardRows.Sum(m => -m.Delta),
            DiscardedCost: discardRows.Sum(m => m.CostAmount ?? 0m),
            DiscardCount: discardRows.Count);

        var sorted = Sort(filteredList, query.SortBy, query.SortDescending);

        var totalCount = sorted.Count;
        var pageItems = sorted
            .Skip(Math.Max(0, (query.Page - 1) * query.PageSize))
            .Take(query.PageSize)
            .ToList();

        var userNames = new Dictionary<Guid, string>();
        userNames[Guid.Empty] = "—"; // discards belong to nobody
        foreach (var uid in pageItems.Select(m => m.UserId).Where(u => u != Guid.Empty).Distinct())
        {
            var user = await _userRepository.GetByIdAsync(new UserId(uid), cancellationToken);
            userNames[uid] = user?.Name ?? "Unknown";
        }

        var entries = pageItems.Select(m => new LedgerEntryDto(
            m.Timestamp, m.Kind, m.Product, m.ProductType, m.Bag, m.Delta, m.Balance,
            m.UserId, userNames.GetValueOrDefault(m.UserId, "Unknown"),
            m.CostAmount, m.CostCurrency, m.Note, m.EntryId, m.CanEdit, m.ProductName, m.ProductBrand)).ToList();

        return Result<LedgerPageDto>.Success(new LedgerPageDto(totalCount, query.Page, query.PageSize, entries, totals));
    }

    private static List<Movement> Sort(List<Movement> rows, string sortBy, bool desc)
    {
        Func<Movement, IComparable> key = (sortBy ?? "ts").ToLowerInvariant() switch
        {
            "kind" => m => m.Kind,
            "product" => m => m.Product,
            "delta" => m => m.Delta,
            "balance" => m => m.Balance,
            "who" => m => m.UserId,
            "cost" => m => m.CostAmount ?? 0m,
            _ => m => m.Timestamp,
        };
        var ordered = desc ? rows.OrderByDescending(key) : rows.OrderBy(key);
        // Stable tiebreaker on timestamp so equal keys are deterministic.
        return ordered.ThenByDescending(m => m.Timestamp).ToList();
    }

    private static (string, string, CoffeeType) ProductKey(CoffeeProduct p) =>
        (p.Name.ToLowerInvariant(), p.Brand.ToLowerInvariant(), p.Type);

    private sealed class Movement
    {
        public DateTime Timestamp { get; init; }
        public string Kind { get; init; } = "";
        public string Product { get; init; } = "";
        public string ProductType { get; init; } = "";
        public string Bag { get; init; } = "";
        public decimal Delta { get; init; }
        public decimal Balance { get; set; }
        public Guid UserId { get; init; }
        public decimal? CostAmount { get; init; }
        public string? CostCurrency { get; init; }
        public string? Note { get; init; }
        public (string, string, CoffeeType) Key { get; init; }
        public Guid? EntryId { get; init; }
        public bool CanEdit { get; init; }
        public string ProductName { get; init; } = "";
        public string ProductBrand { get; init; } = "";
    }
}
