using BeanShare.Domain.Aggregates.CoffeeStock;
using BeanShare.Domain.Common;
using BeanShare.Domain.Services;
using BeanShare.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace BeanShare.Application.Tests.Features;

public sealed class WeightedAverageCostingPolicyTests
{
    private readonly IClock _clock;

    public WeightedAverageCostingPolicyTests()
    {
        _clock = Substitute.For<IClock>();
        _clock.UtcNow.Returns(new DateTime(2025, 10, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateCost_WithThreeDecimalCurrency_ShouldPreserveThirdDecimal()
    {
        // KWD has 3 decimal places. 1.000 KWD for 1000 g => 0.001 KWD/g.
        // Consuming 234 g must cost 0.234 KWD, not 0.23 (which a hardcoded 2-decimal round produces).
        var product = CoffeeProduct.Create("Test Coffee", "Test Brand", CoffeeType.Espresso);
        var purchase = Purchase.Create(
            product,
            Weight.FromGrams(1000m),
            Money.Create(1.000m, "KWD"),
            "Test Vendor",
            new UserId(Guid.NewGuid()),
            _clock.UtcNow,
            _clock);

        var policy = new WeightedAverageCostingPolicy();

        var cost = policy.CalculateCost(new[] { purchase }, Weight.FromGrams(234m), "KWD");

        cost.Amount.Should().Be(0.234m);
    }
}
