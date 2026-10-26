using BeanShare.Application.Common;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.BillingPeriod;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace BeanShare.Application.Tests.Features;

// Rewritten for the money ledger (design-tests §11.2): Paid is now Credit — the stamped cost of cups
// drawn from the payer's bags — and every non-zero position, plus the treasurer, gets a line.
public sealed class SettlementGenerationTests
{
    private static readonly DateTime PeriodStart = new(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime InPeriod = new(2025, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("USD", new DateTime(2025, 11, 5, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _user1;
    private readonly UserId _user2;

    public SettlementGenerationTests()
    {
        _user1 = _world.AddMember("User One");
        _user2 = _world.AddMember("User Two");
        _world.Space.SetTreasurer(_world.Admin);
    }

    private BillingPeriod ClosedPeriod() => _world.AddPeriod("Test Period", PeriodStart, PeriodEnd);

    private void LegacyCupsInPeriod(BillingPeriod period, params (UserId User, decimal Grams)[] cups)
    {
        foreach (var (user, grams) in cups)
            _world.LegacyCup(user, grams, InPeriod).AssignToBillingPeriod(period.Id);
    }

    [Fact]
    public async Task GenerateSettlement_WithClosedPeriod_ShouldSucceed()
    {
        var period = ClosedPeriod();
        _world.AddBag(1000m, 20.00m, InPeriod.AddDays(-1));
        LegacyCupsInPeriod(period, (_user1, 18m), (_user2, 18m));

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        result.Value.BillingPeriodId.Should().Be(period.Id.Value);
        result.Value.Model.Should().Be("Ledger");
        result.Value.Lines.Select(l => l.UserId).Should().BeEquivalentTo(new[] { _user1.Value, _user2.Value, _world.Admin.Value },
            "the buyer is credited and is also the treasurer");
        result.Value.TotalAmount.Should().BeGreaterThan(0);
        _world.Settlements.Should().ContainSingle();
        period.State.Should().Be(BillingState.Settled);
    }

    [Fact]
    public async Task GenerateSettlement_WhenBillingPeriodNotFound_ShouldFail()
    {
        var result = await _world.GenerateAsync(BillingPeriod.Create(_world.Space.Id, "Ghost", PeriodStart, PeriodEnd, _world.Admin, _world.Clock));

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("BILLING_PERIOD_NOT_FOUND");
        _world.Settlements.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateSettlement_WhenPeriodNotClosed_ShouldFail()
    {
        var period = _world.AddPeriod("Test Period", PeriodStart, PeriodEnd, BillingState.Open);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("INVALID_BILLING_PERIOD_STATE");
        _world.Settlements.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateSettlement_WhenSettlementAlreadyExists_ShouldFail()
    {
        var period = ClosedPeriod();
        _world.Settlements.Add(Domain.Aggregates.Settlement.Settlement.Create(_world.Space.Id, period.Id, "USD", _world.Admin, _world.Clock));

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("SETTLEMENT_ALREADY_EXISTS");
    }

    [Fact]
    public async Task GenerateSettlement_WhenUserNotAdmin_ShouldFail()
    {
        var period = ClosedPeriod();
        _world.Actor = _user1;

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("INSUFFICIENT_PRIVILEGES");
        _world.Settlements.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateSettlement_WhenNoConsumptions_ShouldCompleteWithZeroLines()
    {
        var period = ClosedPeriod();

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue("a period with nothing to settle no longer blocks the next one (B8)");
        result.Value.Lines.Should().BeEmpty();
        result.Value.Status.Should().Be(nameof(SettlementStatus.Completed));
    }

    [Fact]
    public async Task GenerateSettlement_ShouldCalculateWeightedAverageCost()
    {
        var period = ClosedPeriod();
        _world.AddBag(1000m, 20.00m, InPeriod.AddDays(-1));
        LegacyCupsInPeriod(period, (_user1, 18m), (_user2, 18m));

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalAmount.Should().Be(36m * 20.00m / 1000m);
    }

    [Fact]
    public async Task GenerateSettlement_ShouldDistributeCostProportionally()
    {
        var period = ClosedPeriod();
        _world.AddBag(1000m, 20.00m, InPeriod.AddDays(-1));
        LegacyCupsInPeriod(period, (_user1, 18m), (_user1, 18m), (_user2, 18m));

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        var user1Line = result.Value.Lines.First(l => l.UserId == _user1.Value);
        var user2Line = result.Value.Lines.First(l => l.UserId == _user2.Value);

        user1Line.TotalCoffeeGrams.Should().Be(36m);
        user2Line.TotalCoffeeGrams.Should().Be(18m);
        user1Line.AmountDue.Should().BeApproximately(user2Line.AmountDue * 2, 0.01m);
        user1Line.ConsumptionPercentage.Should().BeApproximately(66.67m, 0.1m);
        user2Line.ConsumptionPercentage.Should().BeApproximately(33.33m, 0.1m);
    }

    [Fact]
    public async Task GenerateSettlement_ShouldMarkBillingPeriodAsSettled()
    {
        var period = ClosedPeriod();
        var generatedAt = _world.Clock.UtcNow;

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        period.State.Should().Be(BillingState.Settled);
        period.SettledBy.Should().Be(_world.Admin);
        period.SettledAt.Should().Be(generatedAt);
    }

    [Fact]
    public async Task GenerateSettlement_ShouldCreditTheBagPayerWithTheStampedCostOfCupsFromTheirBags()
    {
        var period = ClosedPeriod();
        var bag = _world.AddBag(1000m, 20.00m, InPeriod.AddDays(-1));
        _world.Drink(_user1, 18m, InPeriod, bag);
        _world.Drink(_user2, 18m, InPeriod, bag);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.First(l => l.UserId == _user1.Value).AmountPaid.Should().Be(0m);
        result.Value.Lines.First(l => l.UserId == _user2.Value).AmountPaid.Should().Be(0m);
        result.Value.Lines.First(l => l.UserId == _world.Admin.Value).AmountPaid.Should().Be(0.72m);
    }

    [Fact]
    public async Task GenerateSettlement_BuyerShouldHavePositiveNetBalance()
    {
        var period = ClosedPeriod();
        var bag = _world.AddBag(1000m, 40.00m, InPeriod.AddDays(-1), paidBy: _user2);
        _world.Drink(_user1, 27m, InPeriod, bag);
        _world.Drink(_user2, 9m, InPeriod, bag);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        var user1Line = result.Value.Lines.First(l => l.UserId == _user1.Value);
        var user2Line = result.Value.Lines.First(l => l.UserId == _user2.Value);

        user1Line.AmountPaid.Should().Be(0m);
        user1Line.NetBalance.Should().Be(-1.08m);
        user2Line.AmountPaid.Should().Be(1.44m);
        user2Line.NetBalance.Should().Be(1.08m);
    }

    [Fact]
    public async Task GenerateSettlement_AmountDueShouldSumToTotalAmount()
    {
        var period = ClosedPeriod();
        var bag = _world.AddBag(1000m, 20.00m, InPeriod.AddDays(-1), paidBy: _user1);
        _world.Drink(_user1, 27m, InPeriod, bag);
        _world.Drink(_user2, 9m, InPeriod, bag);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.Sum(l => l.AmountDue).Should().Be(result.Value.TotalAmount);

        var user1Line = result.Value.Lines.First(l => l.UserId == _user1.Value);
        user1Line.AmountPaid.Should().Be(0.72m);
        user1Line.NetBalance.Should().Be(user1Line.AmountPaid - user1Line.AmountDue);
    }

    [Fact]
    public async Task GenerateSettlement_NonConsumerBuyerShouldBeCreditor()
    {
        var period = ClosedPeriod();
        var bag = _world.AddBag(1000m, 20.00m, InPeriod.AddDays(-1));
        _world.Drink(_user1, 18m, InPeriod, bag);
        _world.Drink(_user2, 18m, InPeriod, bag);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.First(l => l.UserId == _user1.Value).NetBalance.Should().BeLessThan(0m);
        result.Value.Lines.First(l => l.UserId == _user2.Value).NetBalance.Should().BeLessThan(0m);
        var buyer = result.Value.Lines.First(l => l.UserId == _world.Admin.Value);
        buyer.NetBalance.Should().Be(0.72m);
        result.Value.Lines.Sum(l => l.NetBalance).Should().Be(0m);
    }

    [Fact]
    public async Task GenerateSettlement_UnattributedCups_ArePricedByTheFallbackRateAndCreditedProRata()
    {
        var period = ClosedPeriod();
        _world.AddBag(1000m, 20.00m, new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        _world.AddBag(500m, 15.00m, new DateTime(2025, 9, 2, 0, 0, 0, DateTimeKind.Utc), paidBy: _user1);
        _world.AddBag(1000m, 99.00m, new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        LegacyCupsInPeriod(period, (_user2, 36m));

        var result = await _world.GenerateAsync(period);

        // Rate over purchases up to the period end only: 35.00 / 1500 g; 36 g = 0.84, credited 20:15.
        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.First(l => l.UserId == _user2.Value).AmountDue.Should().Be(0.84m);
        result.Value.Lines.First(l => l.UserId == _world.Admin.Value).AmountPaid.Should().Be(0.48m);
        result.Value.Lines.First(l => l.UserId == _user1.Value).AmountPaid.Should().Be(0.36m);
        result.Value.Lines.Sum(l => l.NetBalance).Should().Be(0m);
    }

    [Fact]
    public async Task GenerateSettlement_MultipleBuyers_ShouldCalculateCorrectly()
    {
        var period = ClosedPeriod();
        var adminBag = _world.AddBag(1000m, 20.00m, InPeriod.AddDays(-1));
        var user1Bag = _world.AddBag(500m, 15.00m, InPeriod.AddDays(-1), paidBy: _user1);
        _world.Drink(_user1, 27m, InPeriod, adminBag);
        _world.Drink(_user2, 9m, InPeriod, user1Bag);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        var user1Line = result.Value.Lines.First(l => l.UserId == _user1.Value);
        var user2Line = result.Value.Lines.First(l => l.UserId == _user2.Value);
        var adminLine = result.Value.Lines.First(l => l.UserId == _world.Admin.Value);

        user1Line.AmountDue.Should().Be(0.54m);
        user1Line.AmountPaid.Should().Be(0.27m);
        user1Line.NetBalance.Should().Be(-0.27m);
        user2Line.AmountDue.Should().Be(0.27m);
        user2Line.NetBalance.Should().Be(-0.27m);
        adminLine.AmountPaid.Should().Be(0.54m);
        adminLine.NetBalance.Should().Be(0.54m);
        result.Value.TotalAmount.Should().Be(0.81m);
    }

    [Fact]
    public async Task GenerateSettlement_EqualConsumptionUnequalPurchases_ShouldHaveEqualDuesAndCorrectBalances()
    {
        var period = ClosedPeriod();
        var bag = _world.AddBag(1000m, 20.00m, InPeriod.AddDays(-1), paidBy: _user1);
        _world.Drink(_user1, 18m, InPeriod, bag);
        _world.Drink(_user2, 18m, InPeriod, bag);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        var user1Line = result.Value.Lines.First(l => l.UserId == _user1.Value);
        var user2Line = result.Value.Lines.First(l => l.UserId == _user2.Value);

        user1Line.AmountDue.Should().Be(user2Line.AmountDue);
        user1Line.AmountPaid.Should().Be(0.72m);
        user1Line.NetBalance.Should().BeGreaterThan(0m);
        user2Line.AmountPaid.Should().Be(0m);
        user2Line.NetBalance.Should().BeLessThan(0m);
    }

    [Fact]
    public async Task GenerateSettlement_SingleUserDoesEverything_ShouldNetToZero()
    {
        var period = ClosedPeriod();
        var bag = _world.AddBag(1000m, 20.00m, InPeriod.AddDays(-1), paidBy: _user1);
        _world.Drink(_user1, 18m, InPeriod, bag);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        var line = result.Value.Lines.Single(l => l.UserId == _user1.Value);
        line.TotalCoffeeGrams.Should().Be(18m);
        line.AmountDue.Should().Be(result.Value.TotalAmount);
        line.AmountPaid.Should().Be(line.AmountDue);
        line.NetBalance.Should().Be(0m);
        line.IsConfirmed.Should().BeTrue("a zero net needs no payment");
    }

    [Fact]
    public async Task GenerateSettlement_MultipleUsers_AllAmountDueShouldSumToTotalAmount()
    {
        var user3 = _world.AddMember("User Three");
        var period = ClosedPeriod();
        var bag = _world.AddBag(1000m, 20.00m, InPeriod.AddDays(-1));
        var bag2 = _world.AddBag(500m, 15.00m, InPeriod.AddDays(-1), paidBy: _user1);
        _world.Drink(_user1, 27m, InPeriod, bag);
        _world.Drink(_user2, 9m, InPeriod, bag2);
        _world.Drink(user3, 14m, InPeriod, bag);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.Sum(l => l.AmountDue).Should().Be(result.Value.TotalAmount);
        result.Value.Lines.Sum(l => l.NetBalance).Should().Be(0m);
    }

    [Fact]
    public async Task GenerateSettlement_BuyerWithZeroConsumption_GetsACreditLine()
    {
        var period = ClosedPeriod();
        var buyer = _world.AddMember("Buyer");
        var bag = _world.AddBag(1000m, 20.00m, InPeriod.AddDays(-1), paidBy: buyer);
        _world.Drink(_user1, 27m, InPeriod, bag);
        _world.Drink(_user2, 9m, InPeriod, bag);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        var buyerLine = result.Value.Lines.Single(l => l.UserId == buyer.Value);
        buyerLine.TotalCoffeeGrams.Should().Be(0m);
        buyerLine.AmountPaid.Should().Be(0.72m);
        buyerLine.NetBalance.Should().Be(0.72m);
        result.Value.TotalAmount.Should().Be(0.72m);
    }

    [Fact]
    public async Task GenerateSettlement_MultiplePurchasesBySameUser_ShouldSumTheirCredit()
    {
        var period = ClosedPeriod();
        var bagA = _world.AddBag(500m, 10.00m, InPeriod.AddDays(-1), paidBy: _user1);
        var bagB = _world.AddBag(300m, 9.00m, InPeriod.AddDays(-1), paidBy: _user1);
        _world.Drink(_user1, 27m, InPeriod, bagA);
        _world.Drink(_user2, 9m, InPeriod, bagB);

        var result = await _world.GenerateAsync(period);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lines.First(l => l.UserId == _user1.Value).AmountPaid.Should().Be(0.54m + 0.27m);
        result.Value.Lines.First(l => l.UserId == _user2.Value).AmountPaid.Should().Be(0m);
        result.Value.TotalAmount.Should().Be(0.81m);
    }
}
