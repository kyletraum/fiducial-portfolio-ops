using Portfolio.Domain;

namespace Portfolio.Unit;

public class MoneyTests
{
    // S-24: PostgreSQL rounds numeric half AWAY FROM ZERO; .NET's default is half to even.
    // The PostgreSQL side is asserted in spikes/spike-d-sql/04 (round(2.5) = 3,
    // round(-2.5) = -3, round(0.125, 2) = 0.13), run against the migrated schema by
    // run-on-migrations.sh. These are the same midpoints at numeric(19,4)'s scale. Each
    // one is chosen so banker's rounding would give a different answer.
    [Theory]
    [InlineData("1.00025", "1.0003")]   // half-even: 1.0002
    [InlineData("-1.00025", "-1.0003")] // half-even: -1.0002
    [InlineData("2.00005", "2.0001")]   // half-even: 2.0000
    [InlineData("0.12345", "0.1235")]   // half-even: 0.1234
    public void Rounds_a_midpoint_away_from_zero_like_PostgreSQL(string input, string expected)
    {
        var money = new Money(decimal.Parse(input), "USD");

        Assert.Equal(decimal.Parse(expected), money.Amount);
    }

    [Fact]
    public void Default_decimal_rounding_would_disagree()
    {
        // The reason Money names its mode: without it, this is what .NET does.
        Assert.Equal(1.0002m, Math.Round(1.00025m, 4));
        Assert.Equal(1.0003m, new Money(1.00025m, "USD").Amount);
    }

    [Fact]
    public void Adds_amounts_in_the_same_currency()
    {
        var sum = new Money(100.10m, "USD") + new Money(0.0001m, "USD");

        Assert.Equal(new Money(100.1001m, "USD"), sum);
    }

    [Fact]
    public void Refuses_to_combine_different_currencies()
    {
        var usd = new Money(1m, "USD");
        var eur = new Money(1m, "EUR");

        Assert.Throws<CurrencyMismatchException>(() => usd + eur);
        Assert.Throws<CurrencyMismatchException>(() => usd - eur);
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("US1")]
    [InlineData("")]
    public void Refuses_a_currency_that_is_not_an_ISO_code(string currency)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Money(1m, currency));
    }
}
