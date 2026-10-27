using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using BeanShare.Application.Tests.TestHelpers;

namespace BeanShare.Application.Tests.Features.Stock;

// Purchase counters: the write-off-aware restore and the void.
public sealed class PurchaseTests
{
    private readonly MutableClock _clock = new(new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc));

    private Purchase Bag() => Purchase.Create(CoffeeProduct.Create("Alfa", "Roaster", CoffeeType.Filter), Weight.FromGrams(1000m),
        Money.Create(500m, "CZK"), "Store", UserId.New(), _clock.UtcNow.AddDays(-1), _clock);

    [Fact]
    public void DeletingWriteOffShares_LowersTheWriteOffCounter_SoUndoRestoresOnlyWhatIsStillWrittenOff()
    {
        var bag = Bag();
        bag.Consume(Weight.FromGrams(700m), _clock);
        bag.WriteOff(Weight.FromGrams(300m), _clock);

        bag.Restore(Weight.FromGrams(300m), _clock, writeOff: true);
        bag.ConsumedGrams.Should().Be(700m);
        bag.WrittenOffGrams.Should().Be(0m);
        bag.Status.Should().Be(LotStatus.Open, "the bag reopens");

        bag.Discard(_clock).Grams.Should().Be(300m);
        bag.UndoWriteOff(_clock).Grams.Should().Be(300m);
        bag.ConsumedGrams.Should().Be(700m, "the old code restored 600 g and dropped this to 400");
        bag.RemainingGrams.Grams.Should().Be(300m);
    }

    [Fact]
    public void DeletingANormalCup_LeavesTheWriteOffCounter()
    {
        var bag = Bag();
        bag.Consume(Weight.FromGrams(700m), _clock);
        bag.WriteOff(Weight.FromGrams(300m), _clock);

        bag.Restore(Weight.FromGrams(10m), _clock);

        bag.WrittenOffGrams.Should().Be(300m);
        bag.ConsumedGrams.Should().Be(990m);
    }

    [Fact]
    public void VoidWriteOff_MovesGramsToTheVoidedCounter_WithoutReturningThemToStock()
    {
        var bag = Bag();
        bag.Consume(Weight.FromGrams(100m), _clock);
        bag.WriteOff(Weight.FromGrams(900m), _clock);
        var version = bag.Version;

        bag.VoidWriteOff(Weight.FromGrams(900m)).Grams.Should().Be(900m);

        bag.ConsumedGrams.Should().Be(100m);
        bag.WrittenOffGrams.Should().Be(0m);
        bag.VoidedGrams.Should().Be(900m);
        bag.DiscardedGrams.Should().Be(0m);
        bag.RemainingGrams.Grams.Should().Be(0m);
        bag.Status.Should().Be(LotStatus.Empty);
        bag.Version.Should().Be(version + 1);
        bag.IsWriteOffUndoable(_clock.UtcNow).Should().BeFalse();
        var undo = () => bag.UndoWriteOff(_clock);
        undo.Should().Throw<BeanShare.Domain.Exceptions.StockDomainException>();
    }

    [Fact]
    public void VoidWriteOff_IsClampedToTheConsumedGrams()
    {
        var bag = Bag();
        bag.WriteOff(Weight.FromGrams(200m), _clock);

        bag.VoidWriteOff(Weight.FromGrams(500m)).Grams.Should().Be(200m);

        bag.ConsumedGrams.Should().Be(0m);
        bag.VoidedGrams.Should().Be(200m);
        bag.RemainingGrams.Grams.Should().Be(800m);
    }

    [Fact]
    public void AResize_CountsVoidedGramsAsUsed()
    {
        var bag = Bag();
        bag.WriteOff(Weight.FromGrams(400m), _clock);
        bag.VoidWriteOff(Weight.FromGrams(400m));

        var shrink = () => bag.Update(Weight.FromGrams(300m), 500m, bag.PurchasedAt, _clock);

        shrink.Should().Throw<BeanShare.Domain.Exceptions.StockDomainException>();
    }

    [Fact]
    public void ThePaidInExcelFlag_BumpsTheVersionOnlyWhenItChanges()
    {
        var bag = Bag();
        var version = bag.Version;

        bag.SetExcludedFromLedgerCredit(true);
        bag.SetExcludedFromLedgerCredit(true);

        bag.ExcludedFromLedgerCredit.Should().BeTrue();
        bag.Version.Should().Be(version + 1);
    }
}
