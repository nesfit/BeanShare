using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Services;
using BeanShare.Application.Features.Settlement.Commands.ConfirmPayment;
using BeanShare.Application.Features.Settlement.Commands.GenerateSettlement;
using BeanShare.Application.Features.Settlement.Dtos;
using BeanShare.Application.Features.Settlement.Queries.GetSettlementById;
using BeanShare.Application.Features.Settlement.Queries.GetSettlementPreview;
using BeanShare.Application.Features.Settlement.Queries.GetUnsettledSummary;
using BeanShare.Application.Services;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Aggregates.Ledger;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using NSubstitute;
using SettlementAggregate = BeanShare.Domain.Aggregates.Settlement.Settlement;

namespace BeanShare.Application.Tests.TestHelpers;

/// <summary>
/// One space with its cups, bags, entries, periods and settlements held in lists, behind NSubstitute
/// repositories that answer from those lists, so ledger handlers can be exercised end to end.
/// </summary>
public sealed class LedgerWorld
{
    public static readonly CoffeeProduct Product = CoffeeProduct.Create("Alfa", "Roaster", CoffeeType.Filter);

    private readonly Dictionary<UserId, User> _users = new();

    public LedgerWorld(string currency = "CZK", DateTime? now = null)
    {
        Clock = new MutableClock(now ?? new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc));
        Admin = AddUser("Admin");
        Space = Space.Create(SpaceId.New(), "Kava", Currency.Create(currency), Admin, new InviteCode("KAVAXY"), Clock);
        Stock = CoffeeStock.Create(Space.Id, Clock);
        UserContext.CurrentUserId.Returns(_ => Actor);
        Actor = Admin;
        Wire();
    }

    public MutableClock Clock { get; }
    public UserId Admin { get; }
    public UserId Actor { get; set; }
    public Space Space { get; }
    public CoffeeStock Stock { get; }
    public List<ConsumptionEntry> Cups { get; } = new();
    public List<BalanceEntry> Entries { get; } = new();
    public List<BillingPeriod> Periods { get; } = new();
    public List<SettlementAggregate> Settlements { get; } = new();

    public ISpaceRepository SpaceRepository { get; } = Substitute.For<ISpaceRepository>();
    public IConsumptionRepository ConsumptionRepository { get; } = Substitute.For<IConsumptionRepository>();
    public IBalanceEntryRepository BalanceEntryRepository { get; } = Substitute.For<IBalanceEntryRepository>();
    public ISettlementRepository SettlementRepository { get; } = Substitute.For<ISettlementRepository>();
    public ICoffeeStockRepository CoffeeStockRepository { get; } = Substitute.For<ICoffeeStockRepository>();
    public IBillingPeriodRepository BillingPeriodRepository { get; } = Substitute.For<IBillingPeriodRepository>();
    public IUserService UserService { get; } = Substitute.For<IUserService>();
    public IUserContext UserContext { get; } = Substitute.For<IUserContext>();

    public LedgerSnapshotService SnapshotService => new(
        ConsumptionRepository, BalanceEntryRepository, SettlementRepository, CoffeeStockRepository, BillingPeriodRepository, Clock);

    public async Task<Result<SettlementDto>> GenerateAsync(BillingPeriod period)
    {
        var result = await new GenerateSettlementHandler(SettlementRepository, BillingPeriodRepository, SpaceRepository,
            SnapshotService, UserContext, UserService, Clock).Handle(new GenerateSettlementCommand(period.Id.Value), default);
        Clock.Set(Clock.UtcNow.AddMinutes(1));
        return result;
    }

    public Task<Result<SettlementPreviewDto>> PreviewAsync(BillingPeriod period) =>
        new GetSettlementPreviewHandler(SettlementRepository, BillingPeriodRepository, SpaceRepository, SnapshotService, UserService, UserContext)
            .Handle(new GetSettlementPreviewQuery(period.Id.Value), default);

    public Task<Result> ConfirmAsync(SettlementAggregate settlement, UserId member) =>
        new ConfirmPaymentHandler(SettlementRepository, SpaceRepository, UserContext, Clock)
            .Handle(new ConfirmPaymentCommand(settlement.Id.Value, member.Value), default);

    public Task<Result<SettlementDto>> GetSettlementAsync(SettlementAggregate settlement) =>
        new GetSettlementByIdHandler(SettlementRepository, BillingPeriodRepository, SpaceRepository, CoffeeStockRepository, UserService, UserContext)
            .Handle(new GetSettlementByIdQuery(settlement.Id), default);

    public async Task<UnsettledSpaceSummaryDto> UnsettledAsync()
    {
        var result = await new GetUnsettledSummaryHandler(SpaceRepository, BillingPeriodRepository, SnapshotService, UserService, UserContext)
            .Handle(new GetUnsettledSummaryQuery(Space.Id.Value), default);
        return result.Value.Spaces.Single();
    }

    public SettlementAggregate SettlementOf(BillingPeriod period) => Settlements.Single(s => s.BillingPeriodId == period.Id);

    public UserId AddUser(string name)
    {
        var user = User.CreateWithIdAndPassword(Guid.NewGuid(), $"{name.ToLowerInvariant().Replace(' ', '.')}@example.org", name, "hash", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _users[user.Id] = user;
        return user.Id;
    }

    public UserId AddMember(string name, bool admin = false)
    {
        var id = AddUser(name);
        Space.Join(id, Clock);
        if (admin)
            Space.PromoteMember(id, Clock);
        return id;
    }

    public User UserOf(UserId id) => _users[id];

    public Purchase AddBag(decimal grams, decimal cost, DateTime purchasedAt, UserId? paidBy = null, UserId? recordedBy = null)
    {
        var existing = Stock.Purchases.Select(p => p.Id).ToHashSet();
        Stock.AddPurchase(CoffeeProduct.Create(Product.Name, Product.Brand, Product.Type), Weight.FromGrams(grams),
            Money.Create(cost, Space.Currency), "Store", recordedBy ?? Admin, purchasedAt, Clock, paidBy: paidBy);
        return Stock.Purchases.Single(p => !existing.Contains(p.Id));
    }

    /// <summary>A cup drawn from <paramref name="bag"/>, stamped with that bag's cost.</summary>
    public ConsumptionEntry Drink(UserId user, decimal grams, DateTime at, Purchase bag)
    {
        var allocation = Stock.ConsumeStock(CoffeeProduct.Create(Product.Name, Product.Brand, Product.Type), Weight.FromGrams(grams), Clock, bag.Id);
        var cup = ConsumptionEntry.Create(Space.Id, user, CoffeeProduct.Create(Product.Name, Product.Brand, Product.Type),
            Weight.FromGrams(grams), at, Clock, stockLotId: allocation.LotId, cost: allocation.Cost, unitCostPerGram: allocation.UnitCostPerGram);
        Cups.Add(cup);
        return cup;
    }

    /// <summary>A cup with a stamped cost but no bag (its credit goes to the fallback payers).</summary>
    public ConsumptionEntry StampedCup(UserId user, decimal grams, DateTime at, decimal cost)
    {
        var cup = ConsumptionEntry.Create(Space.Id, user, CoffeeProduct.Create(Product.Name, Product.Brand, Product.Type),
            Weight.FromGrams(grams), at, Clock, cost: Money.Create(cost, Space.Currency));
        Cups.Add(cup);
        return cup;
    }

    /// <summary>A legacy cup without a bag or stamped cost (priced by the fallback rate).</summary>
    public ConsumptionEntry LegacyCup(UserId user, decimal grams, DateTime at)
    {
        var cup = ConsumptionEntry.Create(Space.Id, user, CoffeeProduct.Create(Product.Name, Product.Brand, Product.Type),
            Weight.FromGrams(grams), at, Clock);
        Cups.Add(cup);
        return cup;
    }

    public BalanceEntry AddEntry(UserId member, decimal amount, DateTime date, UserId? counterparty = null,
        BalanceEntryKind kind = BalanceEntryKind.Opening, string note = "Excel cutoff")
    {
        var entry = BalanceEntry.Create(Space.Id, member, counterparty ?? Admin, amount, Space.Currency, date, kind, note,
            Admin, Space.LedgerStartDate, Clock);
        Entries.Add(entry);
        return entry;
    }

    public BillingPeriod AddPeriod(string name, DateTime start, DateTime end, BillingState state = BillingState.Closed)
    {
        var period = BillingPeriod.Create(Space.Id, name, start, end, Admin, Clock);
        if (state != BillingState.Draft)
            period.Open(Admin, Clock);
        if (state is BillingState.Closed or BillingState.Settled)
            period.Close(Admin, Clock);
        if (state == BillingState.Settled)
            period.MarkAsSettled(Admin, Clock);
        Periods.Add(period);
        return period;
    }

    private void Wire()
    {
        SpaceRepository.GetSingleBySpecAsync(Arg.Any<ISpec<Space>>(), Arg.Any<CancellationToken>()).Returns(_ => Space);
        SpaceRepository.GetByIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(_ => Space);

        ConsumptionRepository.GetBySpecAsync(Arg.Any<ISpec<ConsumptionEntry>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<ConsumptionEntry>)Cups.Where(ci.Arg<ISpec<ConsumptionEntry>>().Criteria.Compile()).ToList());
        ConsumptionRepository.GetBySpecForUpdateAsync(Arg.Any<ISpec<ConsumptionEntry>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<ConsumptionEntry>)Cups.Where(ci.Arg<ISpec<ConsumptionEntry>>().Criteria.Compile()).ToList());
        ConsumptionRepository.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IEnumerable<ConsumptionEntry>)Cups.ToList());
        ConsumptionRepository.GetByIdAsync(Arg.Any<ConsumptionEntryId>(), Arg.Any<CancellationToken>())
            .Returns(ci => Cups.FirstOrDefault(c => c.Id == ci.Arg<ConsumptionEntryId>()));
        ConsumptionRepository.AddAsync(Arg.Do<ConsumptionEntry>(Cups.Add), Arg.Any<CancellationToken>());

        BalanceEntryRepository.GetUnincludedForUpdateAsync(Arg.Any<SpaceId>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<BalanceEntry>)Entries.Where(e => !e.IsLocked && e.EffectiveDate <= ci.Arg<DateTime>()).ToList());
        BalanceEntryRepository.GetVisibleAsync(Arg.Any<SpaceId>(), Arg.Any<UserId?>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<BalanceEntry>)Entries
                .Where(e => ci.Arg<UserId?>() is not { } id || e.UserId == id || e.CounterpartyUserId == id).ToList());
        BalanceEntryRepository.GetByIdAsync(Arg.Any<BalanceEntryId>(), Arg.Any<CancellationToken>())
            .Returns(ci => Entries.FirstOrDefault(e => e.Id == ci.Arg<BalanceEntryId>()));
        BalanceEntryRepository.AddAsync(Arg.Do<BalanceEntry>(Entries.Add), Arg.Any<CancellationToken>());
        BalanceEntryRepository.When(r => r.Remove(Arg.Any<BalanceEntry>())).Do(ci => Entries.Remove(ci.Arg<BalanceEntry>()));

        SettlementRepository.GetLatestLedgerForUpdateAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>())
            .Returns(_ => Settlements.Where(s => s.IsLedger).OrderByDescending(s => s.Sequence).FirstOrDefault());
        SettlementRepository.GetMaxSequenceAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>())
            .Returns(_ => Settlements.Max(s => s.Sequence) ?? 0);
        SettlementRepository.HasLedgerSettlementAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>())
            .Returns(_ => Settlements.Any(s => s.IsLedger));
        SettlementRepository.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<SettlementAggregate>)Settlements.OrderByDescending(s => s.GeneratedAt).ToList());
        SettlementRepository.GetByBillingPeriodIdAsync(Arg.Any<BillingPeriodId>(), Arg.Any<CancellationToken>())
            .Returns(ci => Settlements.FirstOrDefault(s => s.BillingPeriodId == ci.Arg<BillingPeriodId>()));
        SettlementRepository.GetByIdAsync(Arg.Any<SettlementId>(), Arg.Any<CancellationToken>())
            .Returns(ci => Settlements.FirstOrDefault(s => s.Id == ci.Arg<SettlementId>()));
        SettlementRepository.AddAsync(Arg.Do<SettlementAggregate>(Settlements.Add), Arg.Any<CancellationToken>());

        CoffeeStockRepository.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(_ => Stock);

        BillingPeriodRepository.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<BillingPeriod>)Periods.OrderByDescending(p => p.StartDate).ToList());
        BillingPeriodRepository.GetByIdAsync(Arg.Any<BillingPeriodId>(), Arg.Any<CancellationToken>())
            .Returns(ci => Periods.FirstOrDefault(p => p.Id == ci.Arg<BillingPeriodId>()));
        BillingPeriodRepository.When(r => r.Remove(Arg.Any<BillingPeriod>())).Do(ci => Periods.Remove(ci.Arg<BillingPeriod>()));

        UserService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<User>)ci.Arg<IEnumerable<UserId>>().Where(_users.ContainsKey).Select(id => _users[id]).ToList());
        UserService.GetByIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(ci => _users.GetValueOrDefault(ci.Arg<UserId>()));
        UserService.GetByIdForUpdateAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(ci => _users.GetValueOrDefault(ci.Arg<UserId>()));
    }
}

public sealed class MutableClock(DateTime start) : IClock
{
    public DateTime UtcNow { get; private set; } = start;

    public void Set(DateTime now) => UtcNow = now;
}
