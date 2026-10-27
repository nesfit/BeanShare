using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.CreateBalanceEntry;
using BeanShare.Application.Features.Consumption.Commands;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.Spaces.Commands;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.History;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Enums;
using BeanShare.Domain.ValueObjects;
using Mapster;
using MapsterMapper;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Spaces;

// T-PLH-05 (DS-32): merging a placeholder moves its money by the calculator sweep, never by an entry.
public sealed class MergePlaceholderTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan31 = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb1 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb10 = new(2026, 2, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc));
    private readonly UserId _alice;
    private readonly UserId _placeholder;

    public MergePlaceholderTests()
    {
        _alice = _world.AddMember("Alice");
        _placeholder = _world.AddPlaceholder("Old Pete", "pete@example.org");
        _world.Space.SetTreasurer(_world.Admin);
        _world.AddEntry(_placeholder, -50m, Jan1);
    }

    [Fact]
    public async Task Merge_MovesTheBalanceToTheTarget_WithoutAnEntry_AndHidesThePlaceholder()
    {
        var entriesBefore = _world.Entries.Count;
        var versionBefore = _world.Space.LedgerVersion;

        var result = await _world.MergeAsync(_placeholder, _alice);

        result.IsSuccess.Should().BeTrue();
        _world.Entries.Should().HaveCount(entriesBefore, "the sweep moves the money, not a Correction entry");
        var placeholder = _world.UserOf(_placeholder);
        placeholder.MergedIntoUserId.Should().Be(_alice);
        placeholder.HasDeliverableEmail.Should().BeFalse("the merge released the email");
        _world.Space.LedgerVersion.Should().BeGreaterThan(versionBefore);

        var running = (await _world.SnapshotService.RunningAsync(_world.Space, default)).Calculation;
        running.NetFor(_placeholder).Should().Be(0m);
        running.NetFor(_alice).Should().Be(-50m);
        running.LineFor(_alice)!.MergeTransfer.Should().Be(-50m);
        running.Lines.Sum(l => l.Net).Should().Be(0m);
    }

    [Fact]
    public async Task Merge_MovesHistoryAndThePayerOfEmptyBags_AndBumpsTheLatestSettlement()
    {
        var historicalPeriod = BillingPeriodId.New();
        var import = HistoryImport.Create(_world.Space.Id, _world.Admin, null, _world.Clock);
        import.AddCupCount(historicalPeriod, _placeholder, 7);
        import.AddCupCount(historicalPeriod, _alice, 3);
        import.AddPurchase(historicalPeriod, null, _placeholder, "Beans", null, 100m, "CZK");
        _world.Imports.Add(import);

        var bag = _world.AddBag(10m, 5m, Jan1, paidBy: _placeholder);
        _world.Drink(_alice, 10m, Jan1.AddDays(1), bag);
        await _world.GenerateAsync(_world.AddPeriod("January", Jan1, Jan31));
        var settlement = _world.SettlementOf(_world.Periods.Single());
        var settlementVersion = settlement.Version;

        (await _world.MergeAsync(_placeholder, _alice)).IsSuccess.Should().BeTrue();

        import.CupCounts.Should().ContainSingle(c => c.BillingPeriodId == historicalPeriod)
            .Which.Should().Match<HistoricalCupCount>(c => c.UserId == _alice && c.Cups == 10);
        import.Purchases.Single().PaidByUserId.Should().Be(_alice);
        bag.PaidByUserId.Should().Be(_alice);
        settlement.Version.Should().BeGreaterThan(settlementVersion);
    }

    [Fact]
    public async Task Merge_IsRefused_ForABadTargetOrABagStillHoldingCoffee()
    {
        var otherPlaceholder = _world.AddPlaceholder("Other");
        var outsider = _world.AddUser("Outsider");

        (await _world.MergeAsync(_placeholder, otherPlaceholder)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(Error.Codes.ValidationError);
        (await _world.MergeAsync(_placeholder, outsider)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(Error.Codes.ValidationError);
        (await _world.MergeAsync(_alice, _world.Admin)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(Error.Codes.NotPlaceholder);

        _world.AddBag(1000m, 500m, Jan1, paidBy: _placeholder);
        (await _world.MergeAsync(_placeholder, _alice)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(Error.Codes.MemberHasBalance);

        (await _world.MergeAsync(otherPlaceholder, _alice)).IsSuccess.Should().BeTrue();
        (await _world.MergeAsync(_placeholder, otherPlaceholder)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(Error.Codes.ValidationError, "a merged user is never a target");
    }

    [Fact]
    public async Task AfterTheMerge_ItsLinesAndNewMoneyAreRefused()
    {
        await _world.GenerateAsync(_world.AddPeriod("January", Jan1, Jan31));
        var january = _world.SettlementOf(_world.Periods.Single());
        (await _world.MergeAsync(_placeholder, _alice)).IsSuccess.Should().BeTrue();

        (await _world.ConfirmAsync(january, _placeholder)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(Error.Codes.LineMerged);

        var entry = await new CreateBalanceEntryHandler(
                _world.SpaceRepository, _world.BalanceEntryRepository, _world.UserService, _world.UserContext, _world.Clock, _world.UserRepository)
            .Handle(new CreateBalanceEntryCommand(_world.Space.Id.Value, _placeholder.Value, 10m, Feb10, "late"), default);
        entry.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberMerged);

        _world.AddBag(1000m, 500m, Feb1);
        var cup = await new RecordConsumptionCommandHandler(
                _world.CoffeeStockRepository, _world.ConsumptionRepository, _world.SpaceRepository, _world.UserContext, _world.Clock, _world.UserRepository)
            .Handle(new RecordConsumptionCommand(_world.Space.Id.Value, LedgerWorld.Product.Name, LedgerWorld.Product.Brand,
                LedgerWorld.Product.Type.ToString(), 10m, Feb10, ForUserId: _placeholder.Value), default);
        cup.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberMerged);

        var mapper = new Mapper(new TypeAdapterConfig());
        var purchase = await new AddStockPurchaseCommandHandler(
                _world.CoffeeStockRepository, _world.SpaceRepository, _world.UserContext, _world.Clock, mapper, _world.UserRepository)
            .Handle(new AddStockPurchaseCommand(_world.Space.Id.Value, "Beta", "Roaster", "Filter", 1000m, 400m, "CZK", "Store",
                Feb10, PaidByUserId: _placeholder.Value), default);
        purchase.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberMerged);
    }

    [Fact]
    public async Task TheNextGeneration_SweepsTheCarriedLineAndAnyRevival_ToTheTarget()
    {
        await _world.GenerateAsync(_world.AddPeriod("January", Jan1, Jan31));
        (await _world.MergeAsync(_placeholder, _alice)).IsSuccess.Should().BeTrue();

        // Money that still reaches the placeholder after the merge: a cup logged before it (raced), stamped.
        var bag = _world.AddBag(1000m, 500m, Feb1);
        _world.Drink(_placeholder, 20m, Feb10, bag);

        var february = _world.AddPeriod("February", Feb1, Feb28);
        (await _world.GenerateAsync(february)).IsSuccess.Should().BeTrue();
        var s2 = _world.SettlementOf(february);

        var pete = s2.GetLineForUser(_placeholder)!;
        pete.NetAmount.Should().Be(0m);
        pete.MergeTransferAmount.Should().Be(60m, "carried −50 and a 10.00 cup are swept away");
        pete.Resolution.Should().Be(LineResolution.Confirmed, "a zero line auto-confirms");
        var alice = s2.GetLineForUser(_alice)!;
        alice.MergeTransferAmount.Should().Be(-60m);
        s2.Lines.Sum(l => l.NetAmount ?? 0m).Should().Be(0m);
    }

    [Fact]
    public async Task MergedUsers_GetNoWriteOffShare()
    {
        var bag = _world.AddBag(100m, 50m, Feb1);
        _world.Drink(_placeholder, 10m, Feb10, bag);
        _world.Drink(_alice, 10m, Feb10, bag);
        (await _world.MergeAsync(_placeholder, _alice)).IsSuccess.Should().BeTrue();

        var result = await new MarkBagEmptyCommandHandler(
                _world.CoffeeStockRepository, _world.ConsumptionRepository, _world.SpaceRepository, _world.UserContext, _world.Clock, _world.UserRepository)
            .Handle(new MarkBagEmptyCommand(_world.Space.Id.Value, bag.Id, "Write-off"), default);

        result.IsSuccess.Should().BeTrue();
        _world.Cups.Where(c => c.IsWriteOff).Should().OnlyContain(c => c.UserId == _alice);
    }
    [Fact]
    public async Task RevivalPaths_UndoEmptyAndACupDelete_NeverCreditThePlaceholder()
    {
        var bag = _world.AddBag(1000m, 500m, Feb1, paidBy: _placeholder);
        var cup = _world.Drink(_alice, 100m, Feb10, bag);
        (await new MarkBagEmptyCommandHandler(
                _world.CoffeeStockRepository, _world.ConsumptionRepository, _world.SpaceRepository, _world.UserContext, _world.Clock, _world.UserRepository)
            .Handle(new MarkBagEmptyCommand(_world.Space.Id.Value, bag.Id), default)).IsSuccess.Should().BeTrue();

        (await _world.MergeAsync(_placeholder, _alice)).IsSuccess.Should().BeTrue();
        bag.EffectivePayer.Should().Be(_alice, "an emptied bag it paid for now names the target (V1)");

        (await new UndoBagEmptyCommandHandler(
                _world.CoffeeStockRepository, _world.ConsumptionRepository, _world.BillingPeriodRepository, _world.SpaceRepository, _world.UserContext, _world.Clock)
            .Handle(new UndoBagEmptyCommand(_world.Space.Id.Value, bag.Id), default)).IsSuccess.Should().BeTrue();
        (await new DeleteConsumptionCommandHandler(_world.ConsumptionRepository, _world.BillingPeriodRepository, _world.CoffeeStockRepository, _world.SpaceRepository, _world.Clock)
            .Handle(new DeleteConsumptionCommand(cup.Id.Value, _world.Space.Id.Value), default)).IsSuccess.Should().BeTrue();
        _world.Drink(_world.Admin, 20m, Feb10, bag);

        var february = _world.AddPeriod("February", Feb1, Feb28);
        (await _world.GenerateAsync(february)).IsSuccess.Should().BeTrue();

        var s = _world.SettlementOf(february);
        (s.GetLineForUser(_placeholder)?.NetAmount ?? 0m).Should().Be(0m);
        s.GetLineForUser(_alice)!.NetAmount.Should().Be(-50m + 10m, "the entry swept from the placeholder, and the credit for the admin's cup");
        s.Lines.Sum(l => l.NetAmount ?? 0m).Should().Be(0m);
    }
    [Fact]
    public async Task AWarmUserCacheInTheOtherHead_DoesNotLetAConfirmThrough()
    {
        await _world.GenerateAsync(_world.AddPeriod("January", Jan1, Jan31));
        var january = _world.SettlementOf(_world.Periods.Single());
        // The other head read the placeholder before the merge and still has it cached, unmerged.
        var stale = User.CreatePlaceholder("Old Pete", "pete@example.org", _world.Clock.UtcNow);
        _world.UserService.GetByIdAsync(_placeholder, Arg.Any<CancellationToken>()).Returns(stale);
        _world.UserService.GetByIdsAsync(Arg.Any<IEnumerable<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<User>)ci.Arg<IEnumerable<UserId>>().Select(id => id == _placeholder ? stale : _world.UserOf(id)).ToList());

        (await _world.MergeAsync(_placeholder, _alice)).IsSuccess.Should().BeTrue();

        (await _world.ConfirmAsync(january, _placeholder)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(Error.Codes.LineMerged);
    }
    // A claim or another merge can commit between the merge's uncached reads and its tracked load. The merge (and a
    // placeholder edit) then refuses as its checks would have, instead of letting the domain throw (a 500).
    [Fact]
    public async Task AMerge_FindingThePlaceholderClaimedAfterItsChecks_IsRefused()
    {
        var pete = _world.AddPlaceholder("Pete");
        StaleStatusSays(pete, new UserAccessStatus(true, true, true, null));
        _world.UserOf(pete).ClaimPlaceholder(AuthenticationProvider.Oidc, "idp-pete", _world.Clock.UtcNow);

        var result = await _world.MergeAsync(pete, _alice);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.NotPlaceholder);
        _world.UserOf(pete).IsMerged.Should().BeFalse();
    }

    [Fact]
    public async Task AMerge_FindingThePlaceholderMergedAfterItsChecks_IsRefused()
    {
        var pete = _world.AddPlaceholder("Pete");
        StaleStatusSays(pete, new UserAccessStatus(true, true, true, null));
        _world.UserOf(pete).MarkMergedInto(_world.Admin);

        var result = await _world.MergeAsync(pete, _alice);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberMerged);
        _world.UserOf(pete).MergedIntoUserId.Should().Be(_world.Admin, "the earlier merge stands");
    }

    [Fact]
    public async Task APlaceholderEdit_FindingItMergedAfterItsCheck_IsRefused()
    {
        var pete = _world.AddPlaceholder("Pete");
        StaleStatusSays(pete, new UserAccessStatus(true, true, true, null));
        _world.UserOf(pete).MarkMergedInto(_alice);

        var result = await new UpdatePlaceholderMemberCommandHandler(_world.SpaceRepository, _world.UserRepository, _world.UserService)
            .Handle(new UpdatePlaceholderMemberCommand(_world.Space.Id.Value, pete.Value, "Peter"), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.MemberMerged);
    }

    private void StaleStatusSays(UserId user, UserAccessStatus status) =>
        _world.UserRepository.GetAccessStatusAsync(user, Arg.Any<CancellationToken>()).Returns(status);
}
