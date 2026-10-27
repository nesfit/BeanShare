using System.Globalization;
using BeanShare.Infrastructure.Services.Documents;

namespace BeanShare.Infrastructure.Tests.Services;

// The settlement PDF shows amounts with their currency's own decimals, as the emails do: no ".00" for yen, and the
// third decimal of a dinar is not lost.
public sealed class SettlementPdfAmountTests
{
    [Theory]
    [InlineData("CZK", "1,234.50")]
    [InlineData("JPY", "1,235")]
    [InlineData("KWD", "1,234.500")]
    [InlineData("XXX", "1,234.50")]
    public void AmountsUseTheCurrencysDecimals(string currency, string expected)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            SettlementPdfGenerator.FormatAmount(1234.5m, currency).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
