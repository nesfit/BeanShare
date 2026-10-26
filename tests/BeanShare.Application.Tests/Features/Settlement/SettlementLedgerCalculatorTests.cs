using BeanShare.Domain.Aggregates.Settlement;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Services;
using BeanShare.Domain.ValueObjects;
using NSubstitute;
using SettlementAggregate = BeanShare.Domain.Aggregates.Settlement.Settlement;

namespace BeanShare.Application.Tests.Features.Settlement;

// T-CALC-01..04: the single money calculation (design §4).
public sealed class SettlementLedgerCalculatorTests
{
    public static IEnumerable<object[]> Seeds => Enumerable.Range(1, 200).Select(seed => new object[] { seed });

    private static LedgerCalculationInput RandomScenario(Random random, out UserId treasurer)
    {
        var users = Enumerable.Range(0, random.Next(2, 7)).Select(_ => UserId.New()).ToList();
        var t = users[random.Next(users.Count)];
        treasurer = t;

        var bags = Enumerable.Range(0, random.Next(0, 4))
            .Select(_ => (Payer: users[random.Next(users.Count)], Grams: (decimal)random.Next(250, 1001), Cost: random.Next(50, 900) + random.Next(0, 100) / 100m))
            .ToList();

        var cups = new List<LedgerCup>();
        for (var i = random.Next(0, 40); i > 0; i--)
        {
            var drinker = users[random.Next(users.Count)];
            var grams = (decimal)random.Next(5, 30);
            if (bags.Count > 0 && random.Next(3) > 0)
            {
                var bag = bags[random.Next(bags.Count)];
                cups.Add(new LedgerCup(drinker, grams, Math.Round(bag.Cost / bag.Grams * grams, 2), bag.Payer));
            }
            else
            {
                cups.Add(new LedgerCup(drinker, grams, null, null));
            }
        }

        var fallback = bags.Select(b => new LedgerPurchase(b.Payer, b.Cost, b.Grams)).ToList();

        var entries = new List<LedgerEntry>();
        for (var i = random.Next(0, 6); i > 0; i--)
        {
            var a = users[random.Next(users.Count)];
            var b = users[random.Next(users.Count)];
            if (a != b)
                entries.Add(new LedgerEntry(a, b, random.Next(-60000, 60000) / 100m));
        }

        var carried = new Dictionary<UserId, decimal>();
        foreach (var user in users.Where(u => u != t && random.Next(2) == 0))
            carried[user] = random.Next(-50000, 50000) / 100m;
        if (carried.Count > 0)
            carried[t] = -carried.Values.Sum();

        return new LedgerCalculationInput(cups, fallback, entries, carried, t, 2);
    }

    // T-CALC-01 (I1)
    [Theory]
    [MemberData(nameof(Seeds))]
    public void NetsAlwaysSumToExactlyZero(int seed)
    {
        var input = RandomScenario(new Random(seed), out _);

        var result = SettlementLedgerCalculator.Calculate(input);

        result.Lines.Sum(l => l.Net).Should().Be(0m);
        result.Lines.All(l => decimal.Round(l.Net, 2) == l.Net).Should().BeTrue();
    }

    // T-CALC-01, rev4 (V1, I13): 0–3 merged users per scenario, with their cups, credits, carries and fallback cups.
    [Theory]
    [MemberData(nameof(Seeds))]
    public void MergedUsers_AreSweptToTheirTargets_AndNetsStillSumToZero(int seed)
    {
        var random = new Random(seed);
        var input = RandomScenario(random, out var treasurer);
        var users = input.Cups.Select(c => c.Drinker)
            .Concat(input.Cups.Where(c => c.Payer.HasValue).Select(c => c.Payer!.Value))
            .Concat(input.FallbackPurchases.Select(p => p.Payer))
            .Concat(input.Entries.SelectMany(e => new[] { e.UserId, e.CounterpartyUserId }))
            .Concat(input.CarriedIn.Keys)
            .Where(u => u != treasurer)
            .Distinct()
            .OrderBy(u => u.Value)
            .ToList();
        var merged = users.OrderBy(_ => random.Next()).Take(Math.Min(random.Next(0, 4), Math.Max(0, users.Count - 1))).ToList();
        var targets = users.Except(merged).Append(treasurer).Distinct().ToList();
        var map = merged.ToDictionary(m => m, _ => targets[random.Next(targets.Count)]);

        var before = SettlementLedgerCalculator.Calculate(input);
        var after = SettlementLedgerCalculator.Calculate(input with { MergedInto = map });

        after.Lines.Sum(l => l.Net).Should().Be(0m);
        after.Lines.Sum(l => l.MergeTransfer).Should().Be(0m);
        foreach (var user in merged)
        {
            after.NetFor(user).Should().Be(0m);
            (after.LineFor(user)?.MergeTransfer ?? 0m).Should().Be(-before.NetFor(user));
        }

        foreach (var target in map.Values.Distinct())
        {
            var expected = map.Where(m => m.Value == target).Sum(m => before.NetFor(m.Key));
            (after.LineFor(target)?.MergeTransfer ?? 0m).Should().Be(expected);
            after.NetFor(target).Should().Be(before.NetFor(target) + expected);
        }

        after.FallbackRate.Should().Be(before.FallbackRate);
    }

    // T-CALC-03 (I3) and the residual rule (§4.4)
    [Theory]
    [MemberData(nameof(Seeds))]
    public void StarTransfersSettleEveryLineExactly_AndOnlyTheTreasurerCarriesRounding(int seed)
    {
        var input = RandomScenario(new Random(seed), out var treasurer);

        var result = SettlementLedgerCalculator.Calculate(input);
        var transfers = SettlementLedgerCalculator.StarTransfers(result.Lines.Select(l => (l.UserId, l.Net)), treasurer);

        foreach (var line in result.Lines)
        {
            var paidOut = transfers.Where(t => t.From == line.UserId).Sum(t => t.Amount);
            var received = transfers.Where(t => t.To == line.UserId).Sum(t => t.Amount);
            (paidOut - received).Should().Be(-line.Net);
        }

        transfers.All(t => t.Amount > 0 && (t.From == treasurer || t.To == treasurer)).Should().BeTrue();
        result.Lines.Where(l => l.UserId != treasurer).All(l => l.Rounding == 0m).Should().BeTrue();
        if (result.Lines.Count > 0)
            result.Lines.Should().Contain(l => l.UserId == treasurer);
    }

    // T-CALC-02 (I2)
    [Theory]
    [MemberData(nameof(Seeds))]
    public void CarryOutSumsToZeroForAnyConfirmedOrOpenMix(int seed)
    {
        var random = new Random(seed);
        var input = RandomScenario(random, out var treasurer);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var settlement = SettlementAggregate.CreateLedger(SpaceId.New(), BillingPeriodId.New(), "CZK", treasurer, treasurer, 1, clock);
        foreach (var line in SettlementLedgerCalculator.Calculate(input).Lines)
            settlement.AddLedgerLine(line, null, clock);
        settlement.FinalizeGeneration(clock);

        foreach (var line in settlement.Lines.Where(l => l.IsOpen && l.UserId != treasurer).ToList())
        {
            if (random.Next(2) == 0)
                settlement.ConfirmPayment(line.UserId, treasurer, clock);
        }

        var carryOut = settlement.GetCarryOut();
        carryOut.Values.Sum().Should().Be(0m);
        settlement.CarryForwardTo(SettlementId.New(), clock).Should().BeEquivalentTo(carryOut);
        settlement.Lines.Should().NotContain(l => l.Resolution == LineResolution.Open);
    }

    // T-CALC-04
    [Fact]
    public void ACupFromXsBagDrunkByY_DebitsYAndCreditsX()
    {
        var x = UserId.New();
        var y = UserId.New();

        var result = SettlementLedgerCalculator.Calculate(new LedgerCalculationInput(
            [new LedgerCup(y, 10m, 7.00m, x)], [], [], new Dictionary<UserId, decimal>(), x, 2));

        result.LineFor(y)!.Cost.Should().Be(7.00m);
        result.LineFor(y)!.Net.Should().Be(-7.00m);
        result.LineFor(x)!.Credit.Should().Be(7.00m);
        result.LineFor(x)!.Net.Should().Be(7.00m);
    }

    [Fact]
    public void ADiscardedRemainder_GivesNobodyAnything()
    {
        var treasurer = UserId.New();

        // A discard creates no cups, so there is nothing to debit or credit.
        var result = SettlementLedgerCalculator.Calculate(new LedgerCalculationInput(
            [], [new LedgerPurchase(treasurer, 500m, 1000m)], [], new Dictionary<UserId, decimal>(), treasurer, 2));

        result.Lines.Should().BeEmpty();
    }

    [Fact]
    public void AFallbackCup_IsCreditedProRataToTheWindowsBuyers()
    {
        var a = UserId.New();
        var b = UserId.New();
        var drinker = UserId.New();

        var result = SettlementLedgerCalculator.Calculate(new LedgerCalculationInput(
            [new LedgerCup(drinker, 30m, null, null)],
            [new LedgerPurchase(a, 20.00m, 1000m), new LedgerPurchase(b, 10.00m, 500m)],
            [], new Dictionary<UserId, decimal>(), a, 2));

        result.FallbackRate.Should().Be(0.02m);
        result.LineFor(drinker)!.Cost.Should().Be(0.60m);
        result.LineFor(a)!.Credit.Should().Be(0.40m);
        result.LineFor(b)!.Credit.Should().Be(0.20m);
    }

    [Fact]
    public void WithoutAnyPurchase_AFallbackCupCostsNothing()
    {
        var treasurer = UserId.New();
        var drinker = UserId.New();

        var result = SettlementLedgerCalculator.Calculate(new LedgerCalculationInput(
            [new LedgerCup(drinker, 30m, null, null)], [], [], new Dictionary<UserId, decimal>(), treasurer, 2));

        result.FallbackRate.Should().Be(0m);
        result.LineFor(drinker)!.Cost.Should().Be(0m);
        result.LineFor(drinker)!.CoffeeGrams.Should().Be(30m);
        result.Lines.Sum(l => l.Net).Should().Be(0m);
    }

    [Fact]
    public void ARoundingResidual_LandsOnTheTreasurer()
    {
        var treasurer = UserId.New();
        var a = UserId.New();
        var b = UserId.New();
        var drinker = UserId.New();

        // 1 g at 1/3 per gram, credited 50:50 — each credit rounds 0.1667 to 0.17, the debit to 0.33.
        var result = SettlementLedgerCalculator.Calculate(new LedgerCalculationInput(
            [new LedgerCup(drinker, 1m, null, null)],
            [new LedgerPurchase(a, 1.00m, 3m), new LedgerPurchase(b, 1.00m, 3m)],
            [], new Dictionary<UserId, decimal>(), treasurer, 2));

        result.LineFor(drinker)!.Cost.Should().Be(0.33m);
        result.LineFor(a)!.Credit.Should().Be(0.17m);
        result.LineFor(b)!.Credit.Should().Be(0.17m);
        result.LineFor(treasurer)!.Rounding.Should().Be(-0.01m);
        result.Lines.Sum(l => l.Net).Should().Be(0m);
    }

    [Fact]
    public void EntriesAndCarry_LandOnTheirMembersAndTheTreasurerAlwaysHasALine()
    {
        var treasurer = UserId.New();
        var a = UserId.New();

        var result = SettlementLedgerCalculator.Calculate(new LedgerCalculationInput(
            [],
            [],
            [new LedgerEntry(a, treasurer, -538m)],
            new Dictionary<UserId, decimal> { [a] = -10m, [treasurer] = 10m },
            treasurer,
            2));

        result.LineFor(a)!.Adjustments.Should().Be(-538m);
        result.LineFor(a)!.CarriedIn.Should().Be(-10m);
        result.LineFor(a)!.Net.Should().Be(-548m);
        result.LineFor(treasurer)!.Net.Should().Be(548m);
    }

    // Code round 1, F5 (AD N1).
    [Fact]
    public void WithoutATreasurer_TheResidualIsReportedInsteadOfLost()
    {
        var payer = UserId.New();
        var cups = Enumerable.Range(0, 3).Select(_ => new LedgerCup(UserId.New(), 10m, null, null)).ToList();

        var result = SettlementLedgerCalculator.Calculate(new LedgerCalculationInput(
            cups, [new LedgerPurchase(payer, 100.00m, 30m)], [], new Dictionary<UserId, decimal>(), null, 2));

        result.Lines.Sum(l => l.Net).Should().Be(0.01m);
        result.UnassignedRounding.Should().Be(-0.01m);
        result.Lines.Should().OnlyContain(l => l.Rounding == 0m);
    }
}
