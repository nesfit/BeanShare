using BeanShare.Application.Abstractions;
using BeanShare.Application.Features.ActiveQrCodes.Commands;
using BeanShare.Domain.Aggregates.Space;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace BeanShare.Application.Tests.Features.ActiveQrCodes;

public sealed class ActiveQrCodeBugFixTests
{
    private readonly IActiveQrCodeRepository _qrRepo = Substitute.For<IActiveQrCodeRepository>();
    private readonly ISpaceRepository _spaceRepo = Substitute.For<ISpaceRepository>();
    private readonly IUserContext _userContext = Substitute.For<IUserContext>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly SpaceId _spaceId = SpaceId.New();
    private readonly UserId _adminId = new(Guid.NewGuid());
    private readonly Space _space;

    public ActiveQrCodeBugFixTests()
    {
        _clock.UtcNow.Returns(new DateTime(2025, 10, 15, 0, 0, 0, DateTimeKind.Utc));
        _userContext.CurrentUserId.Returns(_adminId);
        _space = Space.Create(_spaceId, "Test Space", Currency.USD, _adminId, new InviteCode("TESTAB"), _clock);
        _spaceRepo.GetByIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(_space);
    }

    private ActiveQrCode CreateQr(string label = "Kitchen", int grams = 0, string recipe = "")
    {
        var product = CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso);
        return ActiveQrCode.Create(_spaceId, label, product, recipe, grams, _clock);
    }

    // --- Reassign must reject deactivated codes (mirror the scan-resolve guard) ---
    [Fact]
    public async Task Reassign_WhenCodeIsInactive_ReturnsFailure()
    {
        var qr = CreateQr();
        qr.Deactivate();
        _qrRepo.GetByIdAsync(Arg.Any<ActiveQrCodeId>(), Arg.Any<CancellationToken>()).Returns(qr);
        var handler = new ReassignActiveQrCodeCommandHandler(_qrRepo, _spaceRepo, _userContext);

        var result = await handler.Handle(
            new ReassignActiveQrCodeCommand(qr.Id.Value, "Test Coffee", "Test Brand", "Espresso", "", 0),
            default);

        result.IsSuccess.Should().BeFalse();
    }

    // --- Regenerating a QR for the same product+label must replace, not duplicate ---
    [Fact]
    public async Task Create_WhenActiveCodeWithSameProductAndLabelExists_ReplacesInsteadOfDuplicating()
    {
        var existing = CreateQr("Kitchen");
        _qrRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>())
            .Returns(new List<ActiveQrCode> { existing });
        var handler = new CreateActiveQrCodeCommandHandler(_qrRepo, _spaceRepo, _userContext, _clock);

        var result = await handler.Handle(
            new CreateActiveQrCodeCommand(_spaceId.Value, "Kitchen", "Test Coffee", "Test Brand", "Espresso", "Espresso", 8),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(existing.Id.Value, "regenerating keeps the printed QR's id so it still resolves");
        await _qrRepo.DidNotReceive().AddAsync(Arg.Any<ActiveQrCode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WhenLabelDiffers_CreatesNewCode()
    {
        var existing = CreateQr("Kitchen");
        _qrRepo.GetBySpaceIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>())
            .Returns(new List<ActiveQrCode> { existing });
        var handler = new CreateActiveQrCodeCommandHandler(_qrRepo, _spaceRepo, _userContext, _clock);

        var result = await handler.Handle(
            new CreateActiveQrCodeCommand(_spaceId.Value, "Meeting Room", "Test Coffee", "Test Brand", "Espresso", "Espresso", 8),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().NotBe(existing.Id.Value);
        await _qrRepo.Received(1).AddAsync(Arg.Any<ActiveQrCode>(), Arg.Any<CancellationToken>());
    }

    // --- Domain: Label length is bounded (DB column is 100) ---
    [Fact]
    public void UpdateLabel_WhenLongerThan100Chars_Throws()
    {
        var qr = CreateQr();
        var tooLong = new string('x', 101);

        var act = () => qr.UpdateLabel(tooLong);

        act.Should().Throw<ArgumentException>();
    }
}
