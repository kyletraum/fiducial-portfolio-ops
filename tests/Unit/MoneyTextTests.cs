using Portfolio.Domain;

namespace Portfolio.Unit;

/// <summary>
/// MoneyText moved to Domain in slice 02 so the API and the importers share it. Until then it
/// was exercised only through the API; these hold its rules where both now depend on them.
/// </summary>
public class MoneyTextTests
{
    // Expected values as strings: an attribute cannot hold a decimal, and a double rounds
    // numeric(19,4)'s ceiling to 1E+15.
    [Theory]
    [InlineData("0", "0")]
    [InlineData("-250.00", "-250")]
    [InlineData("1234.5678", "1234.5678")]
    [InlineData("999999999999999.9999", "999999999999999.9999")] // numeric(19,4)'s ceiling
    public void Parses_a_plain_decimal_string(string text, string expected)
    {
        Assert.True(MoneyText.TryParse(text, out var amount, out var error), error);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1,234.56")]
    [InlineData("1234,56")]
    [InlineData("+5")]
    [InlineData("1e3")]
    [InlineData(".5")]
    [InlineData("1000000000000000")] // sixteen integer digits
    [InlineData("1.23456")]          // five places: refused, not rounded (R2-M8)
    public void Refuses_anything_else(string? text)
    {
        Assert.False(MoneyText.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Formats_at_scale_4_whatever_the_machine_culture() =>
        Assert.Equal("-1234.5000", Culture.Under("de-DE", () => MoneyText.Format(-1234.5m)));

    [Fact]
    public void Parses_whatever_the_machine_culture()
    {
        var parsed = Culture.Under("de-DE", () => MoneyText.TryParse("1234.56", out var a, out _) ? a : 0);

        Assert.Equal(1234.56m, parsed);
    }
}
