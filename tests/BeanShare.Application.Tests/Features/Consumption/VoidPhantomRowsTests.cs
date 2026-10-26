using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Queries.GetPhantomCandidates;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.Consumption.Commands.VoidPhantomRows;
using BeanShare.Application.Pipeline;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace BeanShare.Application.Tests.Features.Consumption;

// T-CUT-05 (DS-42, I1; J1, J2, J7): voiding phantom rows returns nothing to stock.
public sealed class VoidPhantomRowsTests
{
    private static readonly DateTime C = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc));
    private readonly CapturingLogger<VoidPhantomRowsCommandHandler> _log = new();
    private readonly UserId _alice;
    private readonly Purchase _bag;

    public VoidPhantomRowsTests()
    {
        _alice = _world.AddMember("Alice");
        _world.Space.SetTreasurer(_world.Admin);
        _world.Space.SetLedgerStartDate(C, hasLedgerSettlement: false);
        _bag = _world.AddBag(1000m, 500m, C.AddDays(1));
    }

    /// <summary>Alice drinks 100 g, then the bag is marked empty: 900 g of write-off shares, all hers.</summary>
    private async Task<List<ConsumptionEntry>> WriteOffAsync()
    {
        _world.Drink(_alice, 100m, C.AddDays(2), _bag);
        (await new MarkBagEmptyCommandHandler(
                _world.CoffeeStockRepository, _world.ConsumptionRepository, _world.SpaceRepository, _world.UserContext, _world.Clock, _world.UserRepository)
            .Handle(new MarkBagEmptyCommand(_world.Space.Id.Value, _bag.Id), default)).IsSuccess.Should().BeTrue();
        return _world.Cups.Where(c => c.IsWriteOff).ToList();
    }

    private Task<Result> VoidAsync(params Guid[] ids) =>
        new VoidPhantomRowsCommandHandler(_world.SpaceRepository, _world.ConsumptionRepository, _world.BillingPeriodRepository,
                _world.CoffeeStockRepository, _world.UserContext, _world.Clock, _log)
            .Handle(new VoidPhantomRowsCommand(_world.Space.Id.Value, ids, "phantom fill"), default);

    private ConsumptionEntry SplitRow(UserId user, decimal grams, DateTime at) =>
        AddCup(ConsumptionEntry.Create(_world.Space.Id, user, CoffeeProduct.Create("Alfa", "Roaster", CoffeeType.Filter),
            Weight.FromGrams(grams), at, _world.Clock, presetName: "Split remaining stock"));

    private ConsumptionEntry AddCup(ConsumptionEntry cup)
    {
        _world.Cups.Add(cup);
        return cup;
    }

    [Fact]
    public async Task VoidingAShare_LeavesTheBagEmpty_AndUndoEmptyCannotReviveIt()
    {
        var shares = await WriteOffAsync();
        _bag.IsEmpty.Should().BeTrue();
        var remaining = _bag.RemainingGrams.Grams;
        var version = _bag.Version;
        var ledgerVersion = _world.Space.LedgerVersion;

        (await VoidAsync(shares.Select(s => s.Id.Value).ToArray())).IsSuccess.Should().BeTrue();

        _world.Cups.Should().NotContain(c => c.IsWriteOff);
        _bag.IsEmpty.Should().BeTrue();
        _bag.RemainingGrams.Grams.Should().Be(remaining);
        _bag.ConsumedGrams.Should().Be(100m);
        _bag.WrittenOffGrams.Should().Be(0m);
        _bag.VoidedGrams.Should().Be(900m, "voided grams leave for their own counter, not the discard (J1)");
        _bag.DiscardedGrams.Should().Be(0m);
        _bag.Version.Should().Be(version + shares.Count);
        _world.Space.LedgerVersion.Should().Be(ledgerVersion + 1, "once per command");

        var undo = await new UndoBagEmptyCommandHandler(_world.CoffeeStockRepository, _world.ConsumptionRepository,
                _world.BillingPeriodRepository, _world.SpaceRepository, _world.UserContext, _world.Clock)
            .Handle(new UndoBagEmptyCommand(_world.Space.Id.Value, _bag.Id), default);
        undo.IsSuccess.Should().BeFalse("nothing written off is left to undo");
        _bag.RemainingGrams.Grams.Should().Be(remaining);
        _bag.VoidedGrams.Should().Be(900m);
    }

    [Fact]
    public async Task VoidingAnUnstampedRow_TouchesNoBag_AndIsAudited()
    {
        var split = SplitRow(_alice, 250m, C.AddDays(3));
        var version = _bag.Version;

        (await VoidAsync(split.Id.Value)).IsSuccess.Should().BeTrue();

        _world.Cups.Should().NotContain(split);
        _bag.Version.Should().Be(version);
        _bag.RemainingGrams.Grams.Should().Be(1000m);
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information)
            .Which.Message.Should().Contain("phantom fill").And.Contain(split.Id.Value.ToString());
    }

    [Fact]
    public async Task AStampedNormalCup_IsNotVoidable_AndOneBadIdFailsTheBatch()
    {
        var split = SplitRow(_alice, 250m, C.AddDays(3));
        var cup = _world.Drink(_alice, 10m, C.AddDays(3), _bag);

        (await VoidAsync(split.Id.Value, cup.Id.Value)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.NotVoidable);
        (await VoidAsync(split.Id.Value, Guid.NewGuid())).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.NotFound);

        _world.Cups.Should().Contain(split).And.Contain(cup);
    }

    [Theory]
    [InlineData(BillingState.Closed)]
    [InlineData(BillingState.Settled)]
    public async Task ARowInAClosedOrSettledPeriod_IsRefused(BillingState state)
    {
        var split = SplitRow(_alice, 250m, C.AddDays(3));
        var period = _world.AddPeriod("October", C, C.AddDays(20), state);
        split.AssignToBillingPeriod(period.Id);

        (await VoidAsync(split.Id.Value)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.InvalidBillingPeriodState);
        _world.Cups.Should().Contain(split);
    }

    [Fact]
    public async Task ANonAdmin_IsRefusedByTheAuthorizationBehavior()
    {
        _world.Actor = _alice;
        var behavior = new AuthorizationBehavior<VoidPhantomRowsCommand, Result>(_world.UserContext, _world.SpaceRepository);

        var result = await behavior.Handle(new VoidPhantomRowsCommand(_world.Space.Id.Value, [Guid.NewGuid()], "x"),
            () => Task.FromResult(Result.Success()), default);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TheValidator_AllowsOneToAThousandRowsAndAReason()
    {
        var validator = new VoidPhantomRowsCommandValidator();
        var space = Guid.NewGuid();

        validator.Validate(new VoidPhantomRowsCommand(space, [Guid.NewGuid()], "phantom")).IsValid.Should().BeTrue();
        validator.Validate(new VoidPhantomRowsCommand(space, [], "phantom")).IsValid.Should().BeFalse();
        validator.Validate(new VoidPhantomRowsCommand(space, Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToList(), "phantom")).IsValid.Should().BeFalse();
        validator.Validate(new VoidPhantomRowsCommand(space, [Guid.NewGuid()], " ")).IsValid.Should().BeFalse();
        validator.Validate(new VoidPhantomRowsCommand(space, [Guid.NewGuid()], new string('x', 201))).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Candidates_GroupSharesAndSplitRows_AndLeaveOutSettledAndEarlyRows()
    {
        var shares = await WriteOffAsync();
        var splitA = SplitRow(_alice, 250m, C.AddDays(3).AddSeconds(5));
        var splitB = SplitRow(_world.Admin, 250m, C.AddDays(3).AddSeconds(20));
        var plainBagLess = _world.LegacyCup(_alice, 10m, C.AddDays(4));
        var beforeC = SplitRow(_alice, 250m, C.AddDays(-2));
        var settled = SplitRow(_alice, 250m, C.AddDays(5));
        settled.AssignToBillingPeriod(_world.AddPeriod("Settled", C.AddDays(5), C.AddDays(6), BillingState.Settled).Id);
        var handler = new GetPhantomCandidatesHandler(_world.SpaceRepository, _world.ConsumptionRepository, _world.BillingPeriodRepository,
            _world.CoffeeStockRepository, _world.UserService);

        var byDefault = (await handler.Handle(new GetPhantomCandidatesQuery(_world.Space.Id.Value), default)).Value;

        byDefault.Events.Should().HaveCount(2);
        var split = byDefault.Events.Single(e => e.LooksLikeSplit);
        split.Rows.Select(r => r.ConsumptionId).Should().BeEquivalentTo(new[] { splitA.Id.Value, splitB.Id.Value });
        split.TotalGrams.Should().Be(500m);
        split.CanVoid.Should().BeTrue();
        var writeOff = byDefault.Events.Single(e => e.IsWriteOff);
        writeOff.Rows.Select(r => r.ConsumptionId).Should().BeEquivalentTo(shares.Select(s => s.Id.Value));
        writeOff.BagId.Should().Be(_bag.Id);
        var all = byDefault.Events.SelectMany(e => e.Rows).Select(r => r.ConsumptionId).ToList();
        all.Should().NotContain(new[] { plainBagLess.Id.Value, beforeC.Id.Value, settled.Id.Value });

        var everything = (await handler.Handle(new GetPhantomCandidatesQuery(_world.Space.Id.Value, IncludeAllBagLess: true), default)).Value;
        everything.Events.SelectMany(e => e.Rows).Should().Contain(r => r.ConsumptionId == plainBagLess.Id.Value);
        everything.Events.SelectMany(e => e.Rows).Should().NotContain(r => r.ConsumptionId == settled.Id.Value || r.ConsumptionId == beforeC.Id.Value);
    }
}

/// <summary>Keeps the formatted log entries of one category for assertions.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
