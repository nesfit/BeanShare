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

public sealed class RenameActiveQrCodeTests
{
    private readonly IActiveQrCodeRepository _qrRepo = Substitute.For<IActiveQrCodeRepository>();
    private readonly ISpaceRepository _spaceRepo = Substitute.For<ISpaceRepository>();
    private readonly IUserContext _userContext = Substitute.For<IUserContext>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly SpaceId _spaceId = SpaceId.New();
    private readonly UserId _adminId = new(Guid.NewGuid());
    private readonly Space _space;

    public RenameActiveQrCodeTests()
    {
        _clock.UtcNow.Returns(new DateTime(2025, 10, 15, 0, 0, 0, DateTimeKind.Utc));
        _userContext.CurrentUserId.Returns(_adminId);
        _space = Space.Create(_spaceId, "Test Space", Currency.USD, _adminId, new InviteCode("TESTAB"), _clock);
        _spaceRepo.GetByIdAsync(Arg.Any<SpaceId>(), Arg.Any<CancellationToken>()).Returns(_space);
    }

    private ActiveQrCode CreateQr(string label = "Old Label")
    {
        var product = CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso);
        return ActiveQrCode.Create(_spaceId, label, product, string.Empty, 0, _clock);
    }

    private RenameActiveQrCodeCommandHandler CreateHandler() => new(_qrRepo, _spaceRepo, _userContext);

    [Fact]
    public async Task Rename_WithValidLabel_UpdatesLabelAndKeepsProductAndId()
    {
        var qr = CreateQr("Old Label");
        _qrRepo.GetByIdAsync(Arg.Any<ActiveQrCodeId>(), Arg.Any<CancellationToken>()).Returns(qr);

        var result = await CreateHandler().Handle(new RenameActiveQrCodeCommand(qr.Id.Value, "New Label"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Label.Should().Be("New Label");
        result.Value.Id.Should().Be(qr.Id.Value);
        result.Value.ProductName.Should().Be("Test Coffee");
        qr.Label.Should().Be("New Label");
        await _qrRepo.Received(1).UpdateAsync(qr, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_WithBlankLabel_ReturnsValidationFailureAndLeavesLabelUnchanged()
    {
        var qr = CreateQr("Old Label");
        _qrRepo.GetByIdAsync(Arg.Any<ActiveQrCodeId>(), Arg.Any<CancellationToken>()).Returns(qr);

        var result = await CreateHandler().Handle(new RenameActiveQrCodeCommand(qr.Id.Value, "   "), default);

        result.IsSuccess.Should().BeFalse();
        qr.Label.Should().Be("Old Label");
    }

    [Fact]
    public async Task Rename_WhenCodeNotFound_ReturnsFailure()
    {
        _qrRepo.GetByIdAsync(Arg.Any<ActiveQrCodeId>(), Arg.Any<CancellationToken>()).Returns((ActiveQrCode?)null);

        var result = await CreateHandler().Handle(new RenameActiveQrCodeCommand(Guid.NewGuid(), "New Label"), default);

        result.IsSuccess.Should().BeFalse();
        result.Errors[0].Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Rename_WhenCodeInactive_ReturnsFailure()
    {
        var qr = CreateQr();
        qr.Deactivate();
        _qrRepo.GetByIdAsync(Arg.Any<ActiveQrCodeId>(), Arg.Any<CancellationToken>()).Returns(qr);

        var result = await CreateHandler().Handle(new RenameActiveQrCodeCommand(qr.Id.Value, "New Label"), default);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Rename_WhenNotSpaceMember_ReturnsFailure()
    {
        var qr = CreateQr();
        _qrRepo.GetByIdAsync(Arg.Any<ActiveQrCodeId>(), Arg.Any<CancellationToken>()).Returns(qr);
        _userContext.CurrentUserId.Returns(new UserId(Guid.NewGuid()));

        var result = await CreateHandler().Handle(new RenameActiveQrCodeCommand(qr.Id.Value, "New Label"), default);

        result.IsSuccess.Should().BeFalse();
        await _qrRepo.DidNotReceive().UpdateAsync(Arg.Any<ActiveQrCode>(), Arg.Any<CancellationToken>());
    }
}
