using BeanShare.Application.Common;
using BeanShare.Application.Features.Balances.Commands.UpdateLedgerSettings;
using BeanShare.Application.Features.CoffeeStock.Commands;
using BeanShare.Application.Features.CoffeeStock.Commands.SetBagPaidInExcel;
using BeanShare.Application.Pipeline;
using BeanShare.Application.Tests.TestHelpers;
using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BeanShare.Application.Tests.Features.Stock;

// The "Paid in Excel" flag and the guards that keep it valid.
public sealed class SetBagPaidInExcelTests
{
    private static readonly DateTime LedgerStart = new(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);

    private readonly LedgerWorld _world = new("CZK", new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc));
    private readonly Purchase _old;
    private readonly Purchase _new;

    public SetBagPaidInExcelTests()
    {
        _world.Space.SetTreasurer(_world.Admin);
        _world.Space.SetLedgerStartDate(LedgerStart, hasLedgerSettlement: false);
        _old = _world.AddBag(1000m, 600m, new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc));
        _new = _world.AddBag(1000m, 600m, LedgerStart.AddDays(3));
    }

    private Task<Result> FlagAsync(Purchase bag, bool paidInExcel) =>
        new SetBagPaidInExcelCommandHandler(_world.SpaceRepository, _world.CoffeeStockRepository, _world.SettlementRepository,
                _world.UserContext, _world.Clock, NullLogger<SetBagPaidInExcelCommandHandler>.Instance)
            .Handle(new SetBagPaidInExcelCommand(_world.Space.Id.Value, bag.Id, paidInExcel), default);

    private Task<Result<BeanShare.Application.Features.Balances.Dtos.LedgerSettingsDto>> SetStartAsync(DateTime? start, bool clear = false) =>
        new UpdateLedgerSettingsHandler(_world.SpaceRepository, _world.SettlementRepository, _world.UserService, _world.UserRepository,
                _world.BillingPeriodRepository, _world.CoffeeStockRepository)
            .Handle(new UpdateLedgerSettingsCommand(_world.Space.Id.Value, null, start, ClearLedgerStartDate: clear), default);

    private Task<Result<BeanShare.Application.Features.CoffeeStock.Dtos.StockPurchaseDto>> MoveDateAsync(Purchase bag, DateTime purchasedAt) =>
        new UpdateStockPurchaseCommandHandler(_world.CoffeeStockRepository, _world.SpaceRepository, _world.UserContext, _world.Clock, _world.UserRepository)
            .Handle(new UpdateStockPurchaseCommand(_world.Space.Id.Value, bag.Id, bag.Quantity.Grams, bag.Cost.Amount, purchasedAt), default);

    [Fact]
    public async Task AnAdmin_FlagsAndUnflagsABagBoughtBeforeTheStart_BumpingBothTokens()
    {
        var bagVersion = _old.Version;
        var ledgerVersion = _world.Space.LedgerVersion;

        (await FlagAsync(_old, true)).IsSuccess.Should().BeTrue();
        _old.ExcludedFromLedgerCredit.Should().BeTrue();
        _old.Version.Should().Be(bagVersion + 1);
        _world.Space.LedgerVersion.Should().Be(ledgerVersion + 1);

        (await FlagAsync(_old, false)).IsSuccess.Should().BeTrue();
        _old.ExcludedFromLedgerCredit.Should().BeFalse();
        _world.Space.LedgerVersion.Should().Be(ledgerVersion + 2);
    }

    [Fact]
    public async Task ABagBoughtOnOrAfterTheStart_OrASpaceWithoutAStart_IsRefused()
    {
        (await FlagAsync(_new, true)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ValidationError);

        _world.Space.SetLedgerStartDate(null, hasLedgerSettlement: false);
        (await FlagAsync(_old, true)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.ValidationError);
        _old.ExcludedFromLedgerCredit.Should().BeFalse();
    }

    [Fact]
    public async Task AfterTheFirstLedgerSettlement_TheFlagIsLocked()
    {
        _world.SettlementRepository.HasLedgerSettlementAsync(_world.Space.Id, Arg.Any<CancellationToken>()).Returns(true);

        (await FlagAsync(_old, true)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.LedgerStartLocked);
    }

    [Fact]
    public async Task ANonAdmin_IsRefusedByTheAuthorizationBehavior()
    {
        var member = _world.AddMember("Alice");
        _world.Actor = member;
        var behavior = new AuthorizationBehavior<SetBagPaidInExcelCommand, Result>(_world.UserContext, _world.SpaceRepository);

        var result = await behavior.Handle(new SetBagPaidInExcelCommand(_world.Space.Id.Value, _old.Id, true),
            () => Task.FromResult(Result.Success()), default);

        result.IsFailure.Should().BeTrue();
        _old.ExcludedFromLedgerCredit.Should().BeFalse();
    }

    [Fact]
    public async Task WhileFlagged_TheStartCannotMoveOntoTheBag_NorBeCleared_NorTheBagMoveIntoTheLedger()
    {
        (await FlagAsync(_old, true)).IsSuccess.Should().BeTrue();

        (await SetStartAsync(_old.PurchasedAt.Date)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.BagPaidInExcel);
        (await SetStartAsync(_old.PurchasedAt.Date.AddDays(-1))).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.BagPaidInExcel);
        (await SetStartAsync(null, clear: true)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.BagPaidInExcel);
        (await MoveDateAsync(_old, LedgerStart)).Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Codes.BagPaidInExcel);
        (await MoveDateAsync(_old, LedgerStart.AddDays(1))).Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("correct the opening entry");
        _world.Space.LedgerStartDate.Should().Be(LedgerStart);

        // A start that stays after the bag is fine.
        (await SetStartAsync(_old.PurchasedAt.Date.AddDays(1))).IsSuccess.Should().BeTrue();

        (await FlagAsync(_old, false)).IsSuccess.Should().BeTrue();
        (await SetStartAsync(_old.PurchasedAt.Date)).IsSuccess.Should().BeTrue();
        (await MoveDateAsync(_old, LedgerStart.AddDays(1))).IsSuccess.Should().BeTrue();
        (await SetStartAsync(null, clear: true)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ADateChange_BumpsTheLedgerVersion_ButAnEditWithTheSameDateDoesNot()
    {
        var before = _world.Space.LedgerVersion;

        (await MoveDateAsync(_new, _new.PurchasedAt)).IsSuccess.Should().BeTrue();
        _world.Space.LedgerVersion.Should().Be(before);

        (await MoveDateAsync(_new, _new.PurchasedAt.AddDays(1))).IsSuccess.Should().BeTrue();
        _world.Space.LedgerVersion.Should().Be(before + 1);
    }
}
