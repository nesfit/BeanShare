using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Specifications;

namespace BeanShare.Application.Tests.Features.Settlement;

// A stamped cup moved out of its period is billed exactly once.
public sealed class InclusionPredicateTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan31 = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb1 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));

    public InclusionPredicateTests()
    {
        _world.Space.SetTreasurer(_world.Admin);
    }

    // Port of Probe_CupStampedByClose_MovedPastEndAfterReopen_IsBilledExactlyOnce.
    [Fact]
    public async Task ACupStampedByCloseAndReDatedIntoTheNextPeriod_IsBilledExactlyOnce()
    {
        var alice = _world.AddMember("Alice");
        var bag = _world.AddBag(1000m, 500m, Jan1);
        var january = _world.AddPeriod("January", Jan1, Jan31);
        var cup = _world.Drink(alice, 10m, Jan1.AddDays(19), bag);
        cup.AssignToBillingPeriod(january.Id);
        var february = _world.AddPeriod("February", Feb1, Feb28, BillingState.Open);
        january.Reopen(_world.Admin, _world.Clock, hasLaterPeriod: true);

        var moved = await new UpdateConsumptionCommandHandler(_world.ConsumptionRepository, _world.BillingPeriodRepository,
                _world.CoffeeStockRepository, _world.SpaceRepository, _world.Clock)
            .Handle(new UpdateConsumptionCommand(cup.Id.Value, _world.Space.Id.Value, "Alfa", "Roaster", "Filter", 10m, Feb1.AddDays(4)), default);

        moved.IsSuccess.Should().BeTrue();
        cup.BillingPeriodId.Should().BeNull("the stamp is cleared when the cup leaves its open period");

        january.Close(_world.Admin, _world.Clock);
        var jan = await _world.GenerateAsync(january);
        february.Close(_world.Admin, _world.Clock);
        var feb = await _world.GenerateAsync(february);

        jan.Value.Lines.Should().NotContain(l => l.UserId == alice.Value);
        feb.Value.Lines.Single(l => l.UserId == alice.Value).TotalCoffeeGrams.Should().Be(10m);
        cup.BillingPeriodId.Should().Be(february.Id);
    }

    [Fact]
    public void ACupStampedToAPeriodButDatedAfterItsEnd_IsStillInThatPeriod()
    {
        var alice = _world.AddMember("Alice");
        var january = _world.AddPeriod("January", Jan1, Jan31);
        var cup = _world.LegacyCup(alice, 10m, Feb1.AddDays(3));
        cup.AssignToBillingPeriod(january.Id);

        var spec = new LedgerConsumptionsSpecification(_world.Space.Id, january.EndDate, null, january.Id);

        spec.Criteria.Compile()(cup).Should().BeTrue();
    }

    [Fact]
    public async Task ReDatingWithinTheOpenPeriod_KeepsTheStamp()
    {
        var alice = _world.AddMember("Alice");
        var bag = _world.AddBag(1000m, 500m, Jan1);
        var february = _world.AddPeriod("February", Feb1, Feb28, BillingState.Open);
        var cup = _world.Drink(alice, 10m, Feb1.AddDays(2), bag);
        cup.AssignToBillingPeriod(february.Id);

        var moved = await new UpdateConsumptionCommandHandler(_world.ConsumptionRepository, _world.BillingPeriodRepository,
                _world.CoffeeStockRepository, _world.SpaceRepository, _world.Clock)
            .Handle(new UpdateConsumptionCommand(cup.Id.Value, _world.Space.Id.Value, "Alfa", "Roaster", "Filter", 10m, Feb1.AddDays(9)), default);

        moved.IsSuccess.Should().BeTrue();
        cup.BillingPeriodId.Should().Be(february.Id);
    }

    [Fact]
    public async Task ReDatingBeforeTheLedgerStart_IsRefused()
    {
        var alice = _world.AddMember("Alice");
        var bag = _world.AddBag(1000m, 500m, Jan1);
        _world.Space.SetLedgerStartDate(Feb1, hasLedgerSettlement: false);
        var cup = _world.Drink(alice, 10m, Feb1.AddDays(2), bag);

        var moved = await new UpdateConsumptionCommandHandler(_world.ConsumptionRepository, _world.BillingPeriodRepository,
                _world.CoffeeStockRepository, _world.SpaceRepository, _world.Clock)
            .Handle(new UpdateConsumptionCommand(cup.Id.Value, _world.Space.Id.Value, "Alfa", "Roaster", "Filter", 10m, Jan31), default);

        moved.Errors.Should().ContainSingle().Which.Code.Should().Be("BEFORE_LEDGER_START");
    }

    // The running balance pre-filters in the database again.
    [Fact]
    public void TheRunningPreFilter_SkipsSettledCupsButKeepsUnstampedAndLaterStampedOnes()
    {
        var alice = _world.AddMember("Alice");
        var january = _world.AddPeriod("January", Jan1, Jan31, BillingState.Settled);
        var february = _world.AddPeriod("February", Feb1, Feb28);
        var settledCup = _world.LegacyCup(alice, 10m, Jan1.AddDays(9));
        settledCup.AssignToBillingPeriod(january.Id);
        var unstampedEarly = _world.LegacyCup(alice, 10m, Jan1.AddDays(11));
        var stampedLater = _world.LegacyCup(alice, 10m, Feb1.AddDays(4));
        stampedLater.AssignToBillingPeriod(february.Id);
        var settledReDatedLater = _world.LegacyCup(alice, 10m, Feb1.AddDays(2));
        settledReDatedLater.AssignToBillingPeriod(january.Id);

        var prefilter = new LedgerConsumptionsSpecification(_world.Space.Id, null, null, settledThrough: january.EndDate)
            .Criteria.Compile();
        var settledIds = new HashSet<Domain.ValueObjects.BillingPeriodId> { january.Id };

        prefilter(settledCup).Should().BeFalse();
        prefilter(unstampedEarly).Should().BeTrue();
        prefilter(stampedLater).Should().BeTrue();
        prefilter(settledReDatedLater).Should().BeTrue();
        LedgerConsumptionsSpecification.IsUnsettled(settledReDatedLater, settledIds).Should().BeFalse();
    }
}
