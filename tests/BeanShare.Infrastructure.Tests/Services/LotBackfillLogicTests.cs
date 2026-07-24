using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence.Services;

namespace BeanShare.Infrastructure.Tests.Services;

/// <summary>
/// Pure unit tests for the migration back-fill's per-product FIFO bag-state reconstruction
/// (<see cref="LotBackfillService.ReconstructProductBags"/>). No database required.
/// </summary>
public sealed class LotBackfillLogicTests
{
    private static readonly IClock Clock = new FixedClock(new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc));
    private static readonly UserId Buyer = new(Guid.NewGuid());

    private static Purchase Bag(int grams, int daysAgo) =>
        Purchase.Create(
            CoffeeProduct.Create("Keňa Duma", "Coffeespot", CoffeeType.Filter),
            Weight.FromGrams(grams),
            Money.Create(300m, "CZK"),
            "Store",
            Buyer,
            Clock.UtcNow.AddDays(-daysAgo),
            Clock);

    [Fact]
    public void Reconstruct_FourBagsPartiallyConsumed_MatchesRealKenaDumaShape()
    {
        // Real production shape: 4×1000 g bought over time, 2018 g drunk → the two old bags empty,
        // the third open with 982 g, the fourth still sealed. (This is the "shows 50%" case fixed.)
        var bags = new List<Purchase> { Bag(1000, 100), Bag(1000, 60), Bag(1000, 18), Bag(1000, 18) };

        var overflow = LotBackfillService.ReconstructProductBags(bags, 2018m, Clock);

        overflow.Should().Be(0m);
        bags[0].Label.Should().Be("#1");
        bags[0].Status.Should().Be(LotStatus.Empty);
        bags[1].Status.Should().Be(LotStatus.Empty);
        bags[2].Label.Should().Be("#3");
        bags[2].Status.Should().Be(LotStatus.Open);
        bags[2].RemainingGrams.Should().Be(Weight.FromGrams(982));
        bags[3].Status.Should().Be(LotStatus.Sealed);
        bags[3].RemainingGrams.Should().Be(Weight.FromGrams(1000));
    }

    [Fact]
    public void Reconstruct_OverConsumed_CapsBagsAndReturnsUnattributedOverflow()
    {
        // Etiopie shape: 2×1000 g, 2009 g drunk → both empty, 9 g over-consumption left unattributed.
        var bags = new List<Purchase> { Bag(1000, 100), Bag(1000, 50) };

        var overflow = LotBackfillService.ReconstructProductBags(bags, 2009m, Clock);

        overflow.Should().Be(9m);
        bags.Should().OnlyContain(b => b.Status == LotStatus.Empty);
    }

    [Fact]
    public void Reconstruct_SingleBarelyTouchedBag_IsOpen()
    {
        var bags = new List<Purchase> { Bag(1000, 30) };

        var overflow = LotBackfillService.ReconstructProductBags(bags, 9m, Clock);

        overflow.Should().Be(0m);
        bags[0].Status.Should().Be(LotStatus.Open);
        bags[0].RemainingGrams.Should().Be(Weight.FromGrams(991));
    }

    [Fact]
    public void Reconstruct_NoConsumption_LeavesBagSealed()
    {
        var bags = new List<Purchase> { Bag(1000, 400) };

        var overflow = LotBackfillService.ReconstructProductBags(bags, 0m, Clock);

        overflow.Should().Be(0m);
        bags[0].Label.Should().Be("#1");
        bags[0].Status.Should().Be(LotStatus.Sealed);
    }

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;
    }
}
