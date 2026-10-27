using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Services;
using BeanShare.Domain.ValueObjects;
using BeanShare.Infrastructure.Persistence.Services;
using FluentAssertions;

namespace BeanShare.Infrastructure.Tests.Cutover;

/// <summary>
/// The runbook's worked example on the real lot
/// back-fill, <see cref="CoffeeStock"/>, <see cref="Purchase"/> (with the write-off-aware restore and the void)
/// and <see cref="SettlementLedgerCalculator"/>. Earlier review probes, inverted into the runbook. The "Paid in Excel"
/// input rules of <c>LedgerSnapshotService</c> (flagged bags out of the fallback set, their stamped cups priced as
/// fallback cups) are applied as written; CutoverFallbackTests tests them on the service itself.
/// T is the sole payer and the treasurer; every bag is 1000 g for 600.00 (0.60 per g). Pure: no database.
/// </summary>
public sealed class CutoverRunbookTests
{
    private static readonly DateTime BackfillAt = new(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc);

    /// <summary>Step 5: X in full plus Y's pre-C share, 600.00 × 250 / 1000.</summary>
    private const decimal Excel = 600m + 600m * 250m / 1000m;

    /// <summary>Σ g_pre over the bags opened before C plus Σ Q over the Paid-in-Excel bags.</summary>
    private const decimal Phantom = 250m + 1000m;

    private static CoffeeProduct Beta() => CoffeeProduct.Create("Beta", "Roaster", CoffeeType.Espresso);

    private static DateTime Day(int month, int day) => new(2026, month, day, 9, 0, 0, DateTimeKind.Utc);

    private sealed class Clock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; set; } = now;
    }

    private sealed record Row(UserId Drinker, decimal Grams, Guid? Lot, decimal? Cost, bool WriteOff = false, bool Split = false);

    private sealed class World
    {
        public World() => Stock = CoffeeStock.Create(SpaceId.New(), Clock);

        public Clock Clock { get; } = new(BackfillAt);
        public CoffeeStock Stock { get; }
        public UserId T { get; } = UserId.New();
        public UserId A { get; } = UserId.New();
        public UserId B { get; } = UserId.New();
        public List<Row> Rows { get; } = new();
        public List<LedgerEntry> Entries { get; } = new();

        public decimal CupGrams => Rows.Sum(r => r.Grams);

        public Purchase Bag(DateTime purchasedAt)
        {
            Stock.AddPurchase(Beta(), Weight.FromGrams(1000), Money.Create(600m, "CZK"), "Shop", T, purchasedAt, Clock);
            return Stock.Purchases.Single(p => p.PurchasedAt == purchasedAt);
        }

        /// <summary>Cups from C to 07-23: logged before bags existed, so they have none.</summary>
        public void Unstamped(int count)
        {
            for (var i = 0; i < count; i++)
                Rows.Add(new Row(i % 2 == 0 ? A : B, 10m, null, null));
        }

        /// <summary>A SplitRemainingStock event before 07-24: one unstamped row per member, not IsWriteOff.</summary>
        public void SplitRows(decimal total)
        {
            Rows.Add(new Row(A, total / 2, null, null, Split: true));
            Rows.Add(new Row(B, total - total / 2, null, null, Split: true));
        }

        public void Backfill(decimal loggedGrams)
        {
            Clock.UtcNow = BackfillAt;
            LotBackfillService.ReconstructProductBags(
                Stock.Purchases.OrderBy(p => p.PurchasedAt).ThenBy(p => p.CreatedAt).ToList(), loggedGrams, Clock).Should().Be(0m);
        }

        public void Draw(int count, DateTime at)
        {
            Clock.UtcNow = at;
            for (var i = 0; i < count; i++)
            {
                var allocation = Stock.ConsumeStock(Beta(), Weight.FromGrams(10), Clock);
                Rows.Add(new Row(i % 2 == 0 ? A : B, 10m, allocation.LotId, allocation.Cost.Amount));
            }
        }

        /// <summary>MarkBagEmpty: one write-off share per drinker, each through WriteOffStock.</summary>
        public decimal MarkEmpty(Purchase bag, DateTime at)
        {
            Clock.UtcNow = at;
            var grams = bag.RemainingGrams.Grams;
            foreach (var (who, share) in new[] { (A, grams / 2), (B, grams - grams / 2) })
            {
                var allocation = Stock.WriteOffStock(bag.Id, Weight.FromGrams(share), Clock);
                Rows.Add(new Row(who, share, allocation.LotId, allocation.Cost.Amount, WriteOff: true));
            }

            return grams;
        }

        /// <summary>DeleteConsumption: its own bag for a stamped row (write-off aware), product-wide otherwise.</summary>
        public void Delete(Row row)
        {
            if (row.Lot is { } lot)
                Stock.RestoreStockToLot(lot, Weight.FromGrams(row.Grams), Clock, writeOff: row.WriteOff);
            else
                Stock.RestoreStock(Beta(), Weight.FromGrams(row.Grams), Clock);
            Rows.Remove(row);
        }

        /// <summary>VoidPhantomRows: a share's grams leave for the voided counter; an unstamped row touches no bag.</summary>
        public void Void(Row row)
        {
            if (row.WriteOff && row.Lot is { } lot)
                Stock.VoidWriteOffOnLot(lot, Weight.FromGrams(row.Grams), Clock);
            Rows.Remove(row);
        }

        public decimal Discard(Purchase bag) => Stock.DiscardBag(bag.Id, Clock).Grams;

        /// <summary>Step 6 re-bill: the drinker −amount, counterparty the payer T.</summary>
        public void ReBill(UserId drinker, decimal amount) => Entries.Add(new LedgerEntry(drinker, T, -amount));

        /// <summary>The first settlement, with the "Paid in Excel" input rules.</summary>
        public LedgerCalculation Settle(Purchase paidInExcel)
        {
            Stock.SetPurchaseExcludedFromLedgerCredit(paidInExcel.Id, true, Clock);
            var payers = Stock.Purchases.ToDictionary(p => p.Id, p => p.EffectivePayer);
            var fallback = Stock.Purchases
                .Where(p => !p.ExcludedFromLedgerCredit && p.PurchasedAt <= PeriodEnd)
                .Select(p => new LedgerPurchase(p.EffectivePayer, p.Cost.Amount, p.Quantity.Grams))
                .ToList();
            var cups = Rows
                .Select(c => c.Lot is { } lot && !paidInExcel.Id.Equals(lot) && c.Cost is { } cost
                    ? new LedgerCup(c.Drinker, c.Grams, cost, payers[lot])
                    : new LedgerCup(c.Drinker, c.Grams, null, null))
                .ToList();
            var calc = SettlementLedgerCalculator.Calculate(new LedgerCalculationInput(
                cups, fallback, Entries, new Dictionary<UserId, decimal>(), T, 2));
            calc.Lines.Sum(l => l.Net).Should().Be(0m);
            return calc;
        }

        /// <summary>What T gets in total: the Excel round plus BeanShare's credit and adjustments.</summary>
        public decimal TreasurerTotal(LedgerCalculation calc) => Excel + calc.LineFor(T)!.Credit + calc.LineFor(T)!.Adjustments;

        public decimal DrinkersPay(LedgerCalculation calc) => -(calc.NetFor(A) + calc.NetFor(B));
    }

    /// <summary>
    /// X (03-15) finished before C and Paid in Excel; Y (04-15) opened before C, 250 g drunk before and 750 g
    /// after; Z (06-01). The 1000 g logged before 07-24 have no bag, and the back-fill counts them all on X.
    /// </summary>
    private static (World W, Purchase X, Purchase Y, Purchase Z) ThreeBags()
    {
        var w = new World();
        var x = w.Bag(Day(3, 15));
        var y = w.Bag(Day(4, 15));
        var z = w.Bag(Day(6, 1));
        w.Unstamped(100);
        w.Backfill(1000m);
        x.IsEmpty.Should().BeTrue();
        return (w, x, y, z);
    }

    /// <summary>After 07-24 ConsumeStock opened Y and stamped Z's 750 g to it; in August both were marked empty.</summary>
    private static (World W, Purchase X, Purchase Y, Purchase Z) WorkedExample()
    {
        var (w, x, y, z) = ThreeBags();
        w.Draw(75, Day(7, 25));
        (y.RemainingGrams.Grams, z.RemainingGrams.Grams).Should().Be((250m, 1000m));
        (w.MarkEmpty(y, Day(8, 20)) + w.MarkEmpty(z, Day(8, 20))).Should().Be(Phantom, "every write-off is phantom");
        return (w, x, y, z);
    }

    [Fact]
    public void WorkedExample_VoidingThePhantom_Gives1800And1050_AndNoBagRevives()
    {
        var (w, x, y, z) = WorkedExample();

        foreach (var row in w.Rows.Where(r => r.WriteOff).ToList())
            w.Void(row);

        (y.IsEmpty, z.IsEmpty).Should().Be((true, true));
        (y.VoidedGrams + z.VoidedGrams).Should().Be(Phantom);
        var calc = w.Settle(x);
        w.TreasurerTotal(calc).Should().Be(1800m, "X + Y + Z");
        w.DrinkersPay(calc).Should().Be(1050m, "1750 g after C at 0.60");
        (Phantom + w.CupGrams + 0m + 0m + 0m).Should().Be(3000m, "step 8: phantom + cups + re-billed + real discards + shelf");
    }

    [Fact]
    public void WorkedExample_DeleteThenDiscard_GivesTheSameMoney()
    {
        var (w, x, y, z) = WorkedExample();

        foreach (var row in w.Rows.Where(r => r.WriteOff).ToList())
            w.Delete(row);
        (y.WrittenOffGrams, z.WrittenOffGrams).Should().Be((0m, 0m), "the write-off-aware restore");
        (w.Discard(y) + w.Discard(z)).Should().Be(Phantom);

        var calc = w.Settle(x);
        w.TreasurerTotal(calc).Should().Be(1800m);
        w.DrinkersPay(calc).Should().Be(1050m);
    }

    [Fact]
    public void R8_2_ThePhantomDiscardedInsteadOfWrittenOff_GivesTheSameMoney_AndNoFalseGap()
    {
        var (w, x, y, z) = ThreeBags();
        w.Draw(75, Day(7, 25));
        w.Clock.UtcNow = Day(10, 1);
        (w.Discard(y) + w.Discard(z)).Should().Be(Phantom);

        var calc = w.Settle(x);
        w.TreasurerTotal(calc).Should().Be(1800m);
        w.DrinkersPay(calc).Should().Be(1050m);
        (Phantom + w.CupGrams + 0m + 0m + 0m).Should().Be(3000m, "phantom discards are not counted again");
    }

    [Fact]
    public void Negative_Rev8sFormula_RefundsOnly250g_AndTPaysForXTwice()
    {
        var (w, x, y, _) = WorkedExample();

        foreach (var row in w.Rows.Where(r => r.WriteOff && r.Lot == y.Id).ToList())
            w.Delete(row);
        w.Discard(y);

        var calc = w.Settle(x);
        w.TreasurerTotal(calc).Should().Be(2400m);
        w.DrinkersPay(calc).Should().Be(1650m);
    }

    [Fact]
    public void APartlyPhantomEvent_VoidedAndItsRealPartReBilled_Gives1800And1050()
    {
        var (w, x, y, z) = ThreeBags();
        w.Draw(65, Day(7, 25));                 // 650 g logged after 07-24; 100 g of Z drunk but never logged
        (w.MarkEmpty(y, Day(8, 20)) + w.MarkEmpty(z, Day(8, 20))).Should().Be(1350m, "Y 350 phantom; Z 900 phantom + 100 real");

        foreach (var row in w.Rows.Where(r => r.WriteOff).ToList())
            w.Void(row);
        w.ReBill(w.A, 50m * 0.60m);             // each drinker's share of the 100 real grams × Z's price = 60.00
        w.ReBill(w.B, 50m * 0.60m);

        var calc = w.Settle(x);
        w.TreasurerTotal(calc).Should().Be(1800m);
        w.DrinkersPay(calc).Should().Be(1050m);
        (Phantom + w.CupGrams + 100m + 0m + 0m).Should().Be(3000m, "step 8 with the re-billed grams");
    }

    [Fact]
    public void Step7_AfterAStep6ReBill_AddsNothing()
    {
        var (w, x, y, z) = ThreeBags();
        w.Draw(65, Day(7, 25));
        w.MarkEmpty(y, Day(8, 20));
        w.MarkEmpty(z, Day(8, 20));
        foreach (var row in w.Rows.Where(r => r.WriteOff).ToList())
            w.Void(row);
        w.ReBill(w.A, 30m);
        w.ReBill(w.B, 30m);

        w.DrinkersPay(w.Settle(x)).Should().Be(1050m, "paper 3's extra 100 g were billed once, by the re-bill");

        // Reading step 7 without the re-bill rule would add the same grams again.
        w.Entries.Add(new LedgerEntry(w.A, w.T, -30m));
        w.Entries.Add(new LedgerEntry(w.B, w.T, -30m));
        w.DrinkersPay(w.Settle(x)).Should().Be(1110m);
    }

    /// <summary>
    /// X Paid in Excel, Y opened before C (g_pre 250), W bought 07-22 and in use at the cutover. 750 g logged
    /// from C to 07-20 (from Y); on 07-21 a split billed the "remaining" 1250 g, all phantom; the back-fill pours
    /// 2000 g into X and Y; then 400 g are drawn from W.
    /// </summary>
    private static (World W, Purchase X, Purchase Y, Purchase Wb) SplitWorld()
    {
        var w = new World();
        var x = w.Bag(Day(3, 15));
        var y = w.Bag(Day(4, 15));
        var wb = w.Bag(Day(7, 22));
        w.Unstamped(75);
        w.SplitRows(Phantom);
        w.Backfill(750m + Phantom);
        (x.IsEmpty, y.IsEmpty).Should().Be((true, true));
        w.Draw(40, Day(8, 1));
        wb.RemainingGrams.Grams.Should().Be(600m, "physically 600 g are left in W");
        return (w, x, y, wb);
    }

    [Fact]
    public void SplitWorld_VoidingTheSplitRows_KeepsW_AndWEmptiesWithItsRealCups()
    {
        var (w, x, _, wb) = SplitWorld();

        foreach (var row in w.Rows.Where(r => r.Split).ToList())
            w.Void(row);
        wb.RemainingGrams.Grams.Should().Be(600m);
        w.Draw(60, Day(9, 1));
        wb.IsEmpty.Should().BeTrue("nothing is left to write off");

        var calc = w.Settle(x);
        w.TreasurerTotal(calc).Should().Be(1800m);
        w.DrinkersPay(calc).Should().Be(1050m);
    }

    [Fact]
    public void Negative_SplitWorld_DeletingTheSplitRows_PutsThePhantomIntoW_AndItsMarkEmptyBillsItAgain()
    {
        var (w, x, y, wb) = SplitWorld();

        foreach (var row in w.Rows.Where(r => r.Split).ToList())
            w.Delete(row);
        wb.RemainingGrams.Grams.Should().Be(1000m, "400 g of phantom landed in the bag in use");
        w.Discard(y);
        w.Draw(60, Day(9, 1));
        w.MarkEmpty(wb, Day(9, 20)).Should().Be(400m);

        var calc = w.Settle(x);
        w.TreasurerTotal(calc).Should().Be(2040m);
        w.DrinkersPay(calc).Should().Be(1290m);
    }
}
