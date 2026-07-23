using BeanShare.Domain.Common;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;
using SettlementAggregate = BeanShare.Domain.Aggregates.Settlement.Settlement;

namespace BeanShare.Application.Tests.Features;

public sealed class SettlementRoundingTests
{
    private readonly IClock _clock;

    public SettlementRoundingTests()
    {
        _clock = Substitute.For<IClock>();
        _clock.UtcNow.Returns(new DateTime(2025, 10, 26, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AddLine_WithAmountsThatRound_TotalAmountShouldEqualSumOfLineAmounts()
    {
        var settlement = SettlementAggregate.Create(
            SpaceId.New(),
            new BillingPeriodId(Guid.NewGuid()),
            "USD",
            new UserId(Guid.NewGuid()),
            _clock);

        // Three shares of a 10.00 total: 3.333 + 3.333 + 3.334. Each line stores a value
        // rounded to cents (3.33), so the billed lines sum to 9.99 — the settlement total
        // must reflect what members are actually billed, not the unrounded 10.00.
        settlement.AddLine(new UserId(Guid.NewGuid()), 10m, 3.333m, _clock);
        settlement.AddLine(new UserId(Guid.NewGuid()), 10m, 3.333m, _clock);
        settlement.AddLine(new UserId(Guid.NewGuid()), 10m, 3.334m, _clock);

        var sumOfLines = settlement.Lines.Sum(l => l.AmountDue.Amount);
        settlement.TotalAmount.Should().Be(sumOfLines);
    }
}
