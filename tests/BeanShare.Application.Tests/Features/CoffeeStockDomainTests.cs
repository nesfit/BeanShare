using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Events;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.ValueObjects;

namespace BeanShare.Application.Tests.Features;

public sealed class CoffeeStockDomainTests
{
    private readonly IClock _clock;
    private readonly SpaceId _spaceId;
    private readonly UserId _userId;

    public CoffeeStockDomainTests()
    {
        _clock = TestClock.Instance;
        _spaceId = new SpaceId(Guid.NewGuid());
        _userId = new UserId(Guid.NewGuid());
    }

    [Fact]
    public void CoffeeStock_Create_ShouldCreateWithCorrectProperties()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);

        coffeeStock.Should().NotBeNull();
        coffeeStock.Id.Should().NotBeNull();
        coffeeStock.SpaceId.Should().Be(_spaceId);
        coffeeStock.CreatedAt.Should().Be(_clock.UtcNow);
        coffeeStock.UpdatedAt.Should().Be(_clock.UtcNow);
        coffeeStock.Purchases.Should().BeEmpty();
        coffeeStock.StockLevels.Should().BeEmpty();
    }

    [Fact]
    public void CoffeeStock_Create_ShouldRaiseDomainEvent()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);

        var domainEvent = coffeeStock.DomainEvents.OfType<CoffeeStockCreated>().FirstOrDefault();
        domainEvent.Should().NotBeNull();
        domainEvent?.CoffeeStockId.Should().Be(coffeeStock.Id);
        domainEvent?.SpaceId.Should().Be(_spaceId);
        domainEvent?.OccurredOn.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public void AddPurchase_WithValidData_ShouldAddBagAndCreateStockLevel()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);
        var quantity = Weight.FromGrams(1000);
        var cost = Money.Create(25.99m, "USD");
        var vendor = "Coffee Supplier Inc";
        var purchasedAt = _clock.UtcNow.AddDays(-1);

        coffeeStock.AddPurchase(product, quantity, cost, vendor, _userId, purchasedAt, _clock);

        coffeeStock.Purchases.Should().HaveCount(1);
        coffeeStock.StockLevels.Should().HaveCount(1);

        var bag = coffeeStock.Purchases.First();
        bag.Product.Should().Be(product);
        bag.Quantity.Should().Be(quantity);
        bag.Cost.Should().Be(cost);
        bag.Vendor.Should().Be(vendor);
        bag.PurchasedBy.Should().Be(_userId);
        bag.PurchasedAt.Should().Be(purchasedAt);
        bag.Label.Should().Be("#1");
        bag.Status.Should().Be(LotStatus.Sealed);
        bag.RemainingGrams.Should().Be(quantity);

        var stockLevel = coffeeStock.StockLevels.First();
        stockLevel.Product.Should().Be(product);
        stockLevel.TotalPurchased.Should().Be(quantity);
        stockLevel.TotalConsumed.Should().Be(Weight.Zero);
        stockLevel.CurrentStock.Should().Be(quantity);
    }

    [Fact]
    public void AddPurchase_TwoBagsOfSameProduct_ShouldTrackSeparatelyButRollUp()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Super Crema", "Lavazza", CoffeeType.Espresso);
        var cost = Money.Create(13.50m, "USD");

        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), cost, "Vendor1", _userId, _clock.UtcNow.AddDays(-2), _clock);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), cost, "Vendor2", _userId, _clock.UtcNow.AddDays(-1), _clock);

        // Two separate bags...
        coffeeStock.Purchases.Should().HaveCount(2);
        coffeeStock.Purchases.Select(p => p.Label).Should().Equal("#1", "#2");
        coffeeStock.Purchases.Should().OnlyContain(p => p.RemainingGrams.Grams == 1000);

        // ...that roll up into one per-product view.
        coffeeStock.StockLevels.Should().HaveCount(1);
        coffeeStock.StockLevels.First().TotalPurchased.Should().Be(Weight.FromGrams(2000));
    }

    [Fact]
    public void AddPurchase_WithBagCount_ShouldCreateOneBagPerCount()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Super Crema", "Lavazza", CoffeeType.Espresso);

        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(13.50m, "USD"), "Vendor", _userId, _clock.UtcNow.AddDays(-1), _clock, bagCount: 3);

        coffeeStock.Purchases.Should().HaveCount(3);
        coffeeStock.Purchases.Select(p => p.Label).Should().Equal("#1", "#2", "#3");
        coffeeStock.StockLevels.First().TotalPurchased.Should().Be(Weight.FromGrams(3000));
    }

    [Fact]
    public void AddPurchase_WithFutureDate_ShouldThrowException()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);
        var quantity = Weight.FromGrams(1000);
        var cost = Money.Create(25.99m, "USD");
        var futureDate = _clock.UtcNow.AddDays(1);

        var act = () => coffeeStock.AddPurchase(product, quantity, cost, "Vendor", _userId, futureDate, _clock);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*cannot be in the future*");
    }

    [Fact]
    public void AddPurchase_WithNullProduct_ShouldThrowException()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var quantity = Weight.FromGrams(1000);
        var cost = Money.Create(25.99m, "USD");

        var act = () => coffeeStock.AddPurchase(null!, quantity, cost, "Vendor", _userId, _clock.UtcNow.AddDays(-1), _clock);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddPurchase_ShouldRaiseDomainEvent()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);
        var quantity = Weight.FromGrams(1000);
        var cost = Money.Create(25.99m, "USD");

        // Clear creation event
        coffeeStock.ClearDomainEvents();

        coffeeStock.AddPurchase(product, quantity, cost, "Vendor", _userId, _clock.UtcNow.AddDays(-1), _clock);

        var domainEvent = coffeeStock.DomainEvents.OfType<StockPurchaseAdded>().FirstOrDefault();
        domainEvent.Should().NotBeNull();
        domainEvent?.CoffeeStockId.Should().Be(coffeeStock.Id);
        domainEvent?.Product.Should().Be(product);
        domainEvent?.Quantity.Should().Be(quantity);
        domainEvent?.Cost.Should().Be(cost);
    }

    [Fact]
    public void ConsumeStock_WithValidData_ShouldDrawFromBagAndStampCost()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(20.00m, "USD"), "Vendor", _userId, _clock.UtcNow.AddDays(-1), _clock);

        var allocation = coffeeStock.ConsumeStock(product, Weight.FromGrams(300), _clock);

        var stockLevel = coffeeStock.StockLevels.First();
        stockLevel.TotalConsumed.Should().Be(Weight.FromGrams(300));
        stockLevel.CurrentStock.Should().Be(Weight.FromGrams(700));

        // Cost is stamped from the bag's price ($0.02/g * 300g = $6.00).
        allocation.Cost.Amount.Should().Be(6.00m);
        allocation.UnitCostPerGram.Should().Be(0.02m);
        allocation.LotId.Should().Be(coffeeStock.Purchases.First().Id);
        allocation.ProductRemaining.Should().Be(Weight.FromGrams(700));

        var bag = coffeeStock.Purchases.First();
        bag.Status.Should().Be(LotStatus.Open);
        bag.ConsumedGrams.Should().Be(300);
    }

    [Fact]
    public void ConsumeStock_MoreThanBagHolds_ShouldThrow_NoSpill()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);

        // Two bags of 100 g each. A single cup may never span both.
        coffeeStock.AddPurchase(product, Weight.FromGrams(100), Money.Create(5m, "USD"), "Vendor1", _userId, _clock.UtcNow.AddDays(-2), _clock);
        coffeeStock.AddPurchase(product, Weight.FromGrams(100), Money.Create(5m, "USD"), "Vendor2", _userId, _clock.UtcNow.AddDays(-1), _clock);

        var act = () => coffeeStock.ConsumeStock(product, Weight.FromGrams(150), _clock);

        act.Should().Throw<InsufficientStockException>()
           .WithMessage("*Only*available*");
    }

    [Fact]
    public void ConsumeStock_AcrossBags_ShouldDepleteOldestFirstThenAutoOpenNext()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Super Crema", "Lavazza", CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(12.90m, "USD"), "V1", _userId, _clock.UtcNow.AddDays(-2), _clock);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(13.50m, "USD"), "V2", _userId, _clock.UtcNow.AddDays(-1), _clock);

        var bag1 = coffeeStock.Purchases.First(p => p.Label == "#1");
        var bag2 = coffeeStock.Purchases.First(p => p.Label == "#2");

        coffeeStock.ConsumeStock(product, Weight.FromGrams(1000), _clock); // empties bag #1
        coffeeStock.ConsumeStock(product, Weight.FromGrams(300), _clock);  // auto-opens bag #2

        bag1.Status.Should().Be(LotStatus.Empty);
        bag1.RemainingGrams.Should().Be(Weight.Zero);
        bag2.Status.Should().Be(LotStatus.Open);
        bag2.RemainingGrams.Should().Be(Weight.FromGrams(700));
    }

    [Fact]
    public void ConsumeStock_ProductNotExists_ShouldThrowException()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);

        var act = () => coffeeStock.ConsumeStock(product, Weight.FromGrams(100), _clock);
        act.Should().Throw<ProductNotFoundException>()
           .WithMessage("*not found in stock*");
    }

    [Fact]
    public void ConsumeStock_ShouldRaiseDomainEvent()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(25.99m, "USD"), "Vendor", _userId, _clock.UtcNow.AddDays(-1), _clock);
        coffeeStock.ClearDomainEvents();

        coffeeStock.ConsumeStock(product, Weight.FromGrams(300), _clock);

        var domainEvent = coffeeStock.DomainEvents.OfType<StockConsumed>().FirstOrDefault();
        domainEvent.Should().NotBeNull();
        domainEvent?.CoffeeStockId.Should().Be(coffeeStock.Id);
        domainEvent?.Product.Should().Be(product);
        domainEvent?.ConsumedQuantity.Should().Be(Weight.FromGrams(300));
        domainEvent?.RemainingStock.Should().Be(Weight.FromGrams(700));
    }

    [Fact]
    public void ConsumeStock_BoundToSpecificBag_ShouldDrawFromThatBag()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Super Crema", "Lavazza", CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(12m, "USD"), "V1", _userId, _clock.UtcNow.AddDays(-2), _clock);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(13m, "USD"), "V2", _userId, _clock.UtcNow.AddDays(-1), _clock);
        var bag2 = coffeeStock.Purchases.First(p => p.Label == "#2");

        var allocation = coffeeStock.ConsumeStock(product, Weight.FromGrams(200), _clock, targetLotId: bag2.Id);

        allocation.LotId.Should().Be(bag2.Id);
        bag2.ConsumedGrams.Should().Be(200);
        coffeeStock.Purchases.First(p => p.Label == "#1").ConsumedGrams.Should().Be(0);
    }

    [Fact]
    public void ConsumeStock_BoundToEmptyBag_ShouldFallBackToOpenOrOldest()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Super Crema", "Lavazza", CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(200), Money.Create(4m, "USD"), "V1", _userId, _clock.UtcNow.AddDays(-2), _clock);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(13m, "USD"), "V2", _userId, _clock.UtcNow.AddDays(-1), _clock);
        var bag1 = coffeeStock.Purchases.First(p => p.Label == "#1");
        coffeeStock.ConsumeStock(product, Weight.FromGrams(200), _clock, bag1.Id); // fully consume bag #1

        // QR bound to the now-empty bag #1 must fall back rather than fail.
        var allocation = coffeeStock.ConsumeStock(product, Weight.FromGrams(100), _clock, targetLotId: bag1.Id);

        allocation.LotId.Should().Be(coffeeStock.Purchases.First(p => p.Label == "#2").Id);
    }

    [Fact]
    public void GetLowStockProducts_WithThreshold_ShouldReturnProductsBelowThreshold()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product1 = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);
        var product2 = CoffeeProduct.Create("House Roast", "Local Roasters", CoffeeType.Filter);

        coffeeStock.AddPurchase(product1, Weight.FromGrams(1000), Money.Create(25.99m, "USD"), "Vendor", _userId, _clock.UtcNow.AddDays(-1), _clock);
        coffeeStock.AddPurchase(product2, Weight.FromGrams(500), Money.Create(15.50m, "USD"), "Vendor", _userId, _clock.UtcNow.AddDays(-1), _clock);

        coffeeStock.ConsumeStock(product1, Weight.FromGrams(950), _clock);
        coffeeStock.ConsumeStock(product2, Weight.FromGrams(100), _clock);

        var lowStockProducts = coffeeStock.GetLowStockProducts(Weight.FromGrams(100)).ToList();

        lowStockProducts.Should().HaveCount(1);
        lowStockProducts.First().Product.Should().Be(product1);
        lowStockProducts.First().CurrentStock.Should().Be(Weight.FromGrams(50));
    }

    [Fact]
    public void GetLowStockProducts_WithHighThreshold_ShouldReturnAllProducts()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);

        coffeeStock.AddPurchase(product, Weight.FromGrams(500), Money.Create(25.99m, "USD"), "Vendor", _userId, _clock.UtcNow.AddDays(-1), _clock);

        var lowStockProducts = coffeeStock.GetLowStockProducts(Weight.FromGrams(1000)).ToList();

        lowStockProducts.Should().HaveCount(1);
        lowStockProducts.First().CurrentStock.Should().Be(Weight.FromGrams(500));
    }

    [Fact]
    public void UpdatePurchase_IncreasingQuantity_ShouldGrowBag()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);
        var purchasedAt = _clock.UtcNow.AddDays(-1);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(20m, "USD"), "Vendor", _userId, purchasedAt, _clock);
        var purchaseId = coffeeStock.Purchases.First().Id;

        coffeeStock.UpdatePurchase(purchaseId, Weight.FromGrams(1500), 30m, purchasedAt, _clock);

        var stock = coffeeStock.StockLevels.First();
        stock.TotalPurchased.Should().Be(Weight.FromGrams(1500));
        stock.CurrentStock.Should().Be(Weight.FromGrams(1500));
        coffeeStock.Purchases.First().Quantity.Should().Be(Weight.FromGrams(1500));
    }

    [Fact]
    public void UpdatePurchase_DecreasingBelowConsumed_ShouldThrow()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);
        var purchasedAt = _clock.UtcNow.AddDays(-1);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(20m, "USD"), "Vendor", _userId, purchasedAt, _clock);
        coffeeStock.ConsumeStock(product, Weight.FromGrams(700), _clock);
        var purchaseId = coffeeStock.Purchases.First().Id;

        var act = () => coffeeStock.UpdatePurchase(purchaseId, Weight.FromGrams(100), 2m, purchasedAt, _clock);

        act.Should().Throw<StockDomainException>().WithMessage("*already been consumed*");
    }

    [Fact]
    public void UpdatePurchase_ChangingProduct_ShouldMoveBagToNewProduct()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var oldProduct = CoffeeProduct.Create("Pike Place", "Starbucks", CoffeeType.Filter);
        var newProduct = CoffeeProduct.Create("Super Crema", "Lavazza", CoffeeType.Espresso);
        var purchasedAt = _clock.UtcNow.AddDays(-1);
        coffeeStock.AddPurchase(oldProduct, Weight.FromGrams(800), Money.Create(16m, "USD"), "Vendor", _userId, purchasedAt, _clock);
        var purchaseId = coffeeStock.Purchases.First().Id;

        coffeeStock.UpdatePurchase(
            purchaseId,
            Weight.FromGrams(800),
            16m,
            purchasedAt,
            _clock,
            newProductName: newProduct.Name,
            newProductBrand: newProduct.Brand,
            newCoffeeType: newProduct.Type);

        // The old product no longer has any bags, so it drops off the rollup entirely.
        coffeeStock.StockLevels.Should().ContainSingle();
        coffeeStock.StockLevels.Single().Product.Should().Be(newProduct);
        coffeeStock.StockLevels.Single().CurrentStock.Should().Be(Weight.FromGrams(800));
        coffeeStock.GetCurrentStock(oldProduct).Should().Be(Weight.Zero);
        coffeeStock.Purchases.Single().Product.Should().Be(newProduct);
    }

    [Fact]
    public void UpdatePurchase_NonexistentPurchase_ShouldThrow()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var purchasedAt = _clock.UtcNow.AddDays(-1);

        var act = () => coffeeStock.UpdatePurchase(Guid.NewGuid(), Weight.FromGrams(100), 5m, purchasedAt, _clock);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public void DeletePurchase_WithUntouchedBag_ShouldRemoveIt()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);
        var purchasedAt = _clock.UtcNow.AddDays(-1);
        coffeeStock.AddPurchase(product, Weight.FromGrams(600), Money.Create(12m, "USD"), "Vendor", _userId, purchasedAt, _clock);
        var purchaseId = coffeeStock.Purchases.First().Id;

        coffeeStock.DeletePurchase(purchaseId, _clock);

        coffeeStock.Purchases.Should().BeEmpty();
        coffeeStock.StockLevels.Should().BeEmpty();
    }

    [Fact]
    public void DeletePurchase_WithPartiallyConsumedBag_ShouldThrow()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Premium Blend", "Blue Mountain", CoffeeType.Espresso);
        var purchasedAt = _clock.UtcNow.AddDays(-1);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(20m, "USD"), "Vendor", _userId, purchasedAt, _clock);
        coffeeStock.ConsumeStock(product, Weight.FromGrams(400), _clock);
        var purchaseId = coffeeStock.Purchases.First().Id;

        var act = () => coffeeStock.DeletePurchase(purchaseId, _clock);

        act.Should().Throw<StockDomainException>().WithMessage("*already been consumed*");
    }

    [Fact]
    public void DeletePurchase_NonexistentPurchase_ShouldThrow()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);

        var act = () => coffeeStock.DeletePurchase(Guid.NewGuid(), _clock);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public void OpenBag_ShouldMakeItTheBagCupsDrawFrom()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Colombian Supremo", "Counter Culture", CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(22m, "USD"), "Importer", _userId, _clock.UtcNow.AddDays(-2), _clock);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(23m, "USD"), "Importer", _userId, _clock.UtcNow.AddDays(-1), _clock);
        var bag2 = coffeeStock.Purchases.First(p => p.Label == "#2");

        coffeeStock.OpenBag(bag2.Id, _clock);
        var allocation = coffeeStock.ConsumeStock(product, Weight.FromGrams(50), _clock);

        allocation.LotId.Should().Be(bag2.Id);
        bag2.IsOpen.Should().BeTrue();
        coffeeStock.StockLevels.First().IsCurrentlyUsed.Should().BeTrue();
    }

    [Fact]
    public void OpenBag_OnEmptyBag_ShouldThrow()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Guatemalan Antigua", "Verve", CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(500), Money.Create(16m, "USD"), "Distributor", _userId, _clock.UtcNow.AddDays(-1), _clock);
        var bagId = coffeeStock.Purchases.First().Id;
        coffeeStock.ConsumeStock(product, Weight.FromGrams(500), _clock, bagId); // fully consume the bag

        var act = () => coffeeStock.OpenBag(bagId, _clock);

        act.Should().Throw<StockDomainException>().WithMessage("*empty*");
    }

    [Fact]
    public void RestoreStockToLot_ShouldReverseADraw()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Kenyan AA", "Heart Roasters", CoffeeType.Filter);
        coffeeStock.AddPurchase(product, Weight.FromGrams(500), Money.Create(19m, "USD"), "Importer", _userId, _clock.UtcNow.AddDays(-1), _clock);
        var allocation = coffeeStock.ConsumeStock(product, Weight.FromGrams(120), _clock);

        coffeeStock.RestoreStockToLot(allocation.LotId, Weight.FromGrams(120), _clock);

        coffeeStock.GetCurrentStock(product).Should().Be(Weight.FromGrams(500));
        coffeeStock.Purchases.First().ConsumedGrams.Should().Be(0);
    }

    [Fact]
    public void ConsumeStock_WithFractionalPerGramPrice_ShouldRoundCostOnce()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Super Crema", "Lavazza", CoffeeType.Espresso);
        // €13.50 / 1000 g = 0.0135/g — a non-terminating-at-2dp per-gram price.
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(13.50m, "USD"), "V", _userId, _clock.UtcNow.AddDays(-1), _clock);

        var allocation = coffeeStock.ConsumeStock(product, Weight.FromGrams(250), _clock);

        allocation.UnitCostPerGram.Should().Be(0.0135m);
        // Round-once: 0.0135 * 250 = 3.375 -> 3.38 (a round-per-gram bug would give 0.01*250 = 2.50).
        allocation.Cost.Amount.Should().Be(3.38m);
    }

    [Fact]
    public void ConsumeStock_WithTwoOpenBags_ShouldDrawFromMostRecentlyOpened()
    {
        var clock = new AdvanceableClock(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc));
        var coffeeStock = CoffeeStock.Create(_spaceId, clock);
        var product = CoffeeProduct.Create("Super Crema", "Lavazza", CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(12m, "USD"), "V1", _userId, clock.UtcNow.AddDays(-2), clock);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(13m, "USD"), "V2", _userId, clock.UtcNow.AddDays(-1), clock);
        var bag1 = coffeeStock.Purchases.First(p => p.Label == "#1");
        var bag2 = coffeeStock.Purchases.First(p => p.Label == "#2");

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        coffeeStock.OpenBag(bag1.Id, clock);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        coffeeStock.OpenBag(bag2.Id, clock); // bag2 opened most recently -> it is the active bag

        coffeeStock.ConsumeStock(product, Weight.FromGrams(50), clock).LotId.Should().Be(bag2.Id);

        // Re-open bag1 last -> it becomes the active bag (reverse-order mirror).
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        coffeeStock.OpenBag(bag1.Id, clock);
        coffeeStock.ConsumeStock(product, Weight.FromGrams(50), clock).LotId.Should().Be(bag1.Id);
    }

    [Fact]
    public void RestoreStock_ProductLevel_ShouldRefillNewestBagFirst()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Super Crema", "Lavazza", CoffeeType.Espresso);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(12m, "USD"), "V1", _userId, _clock.UtcNow.AddDays(-2), _clock);
        coffeeStock.AddPurchase(product, Weight.FromGrams(1000), Money.Create(13m, "USD"), "V2", _userId, _clock.UtcNow.AddDays(-1), _clock);
        var bag1 = coffeeStock.Purchases.First(p => p.Label == "#1");
        var bag2 = coffeeStock.Purchases.First(p => p.Label == "#2");

        coffeeStock.ConsumeStock(product, Weight.FromGrams(1000), _clock); // empties bag #1
        coffeeStock.ConsumeStock(product, Weight.FromGrams(200), _clock);  // draws 200 from bag #2

        // Product-level restore of 300 refills the newest bag first: bag #2 (200) then bag #1 (100).
        coffeeStock.RestoreStock(product, Weight.FromGrams(300), _clock);

        bag2.ConsumedGrams.Should().Be(0);
        bag1.ConsumedGrams.Should().Be(900);
    }

    [Fact]
    public void RestoreStockToLot_OnEmptyBag_ShouldReviveToOpenAndClearDepletedAt()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var product = CoffeeProduct.Create("Kenyan AA", "Heart Roasters", CoffeeType.Filter);
        coffeeStock.AddPurchase(product, Weight.FromGrams(300), Money.Create(9m, "USD"), "V", _userId, _clock.UtcNow.AddDays(-1), _clock);
        var allocation = coffeeStock.ConsumeStock(product, Weight.FromGrams(300), _clock); // fully consumes the bag
        var bag = coffeeStock.Purchases.First();
        bag.Status.Should().Be(LotStatus.Empty);
        bag.DepletedAt.Should().NotBeNull();

        coffeeStock.RestoreStockToLot(allocation.LotId, Weight.FromGrams(100), _clock);

        bag.Status.Should().Be(LotStatus.Open);
        bag.DepletedAt.Should().BeNull();

        // The revived bag can be drawn from again.
        coffeeStock.ConsumeStock(product, Weight.FromGrams(50), _clock);
        bag.ConsumedGrams.Should().Be(250);
    }

    [Fact]
    public void UpdatePurchase_MovingIntoProductWithExistingBag_ShouldLabelSequentially()
    {
        var coffeeStock = CoffeeStock.Create(_spaceId, _clock);
        var target = CoffeeProduct.Create("Super Crema", "Lavazza", CoffeeType.Espresso);
        var other = CoffeeProduct.Create("Pike Place", "Starbucks", CoffeeType.Filter);
        var at = _clock.UtcNow.AddDays(-1);
        coffeeStock.AddPurchase(target, Weight.FromGrams(1000), Money.Create(12m, "USD"), "V", _userId, at, _clock); // target "#1"
        coffeeStock.AddPurchase(other, Weight.FromGrams(500), Money.Create(8m, "USD"), "V", _userId, at, _clock);    // other "#1"
        var otherBagId = coffeeStock.Purchases.First(p => p.Product.Equals(other)).Id;

        coffeeStock.UpdatePurchase(otherBagId, Weight.FromGrams(500), 8m, at, _clock,
            newProductName: target.Name, newProductBrand: target.Brand, newCoffeeType: target.Type);

        coffeeStock.Purchases.First(p => p.Id == otherBagId).Label.Should().Be("#2");
        coffeeStock.Purchases.Where(p => p.Product.Equals(target)).Select(p => p.Label)
            .Should().BeEquivalentTo(new[] { "#1", "#2" });
    }

    private sealed class AdvanceableClock : IClock
    {
        public AdvanceableClock(DateTime start) => UtcNow = start;
        public DateTime UtcNow { get; set; }
    }
}
