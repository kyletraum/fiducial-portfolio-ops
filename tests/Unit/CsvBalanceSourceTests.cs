using System.Text;
using Portfolio.Ingestion;

namespace Portfolio.Unit;

/// <summary>slice-02.md step 1: the platform's own CSV, read strictly. Invented values only.</summary>
public class CsvBalanceSourceTests
{
    static ParseResult Parse(string text) => new CsvBalanceSource().Parse(Encoding.UTF8.GetBytes(text));

    static ParseError OnlyError(ParseResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Empty(result.Balances);
        return Assert.Single(result.Errors);
    }

    [Fact]
    public void Reads_a_file_as_written()
    {
        var result = Parse("account,as_of_date,balance\nEXAMPLE-CHK-1,2026-10-15,1234.56\nEXAMPLE-CC-1,2026-10-15,-250.00\n");

        Assert.True(result.Succeeded);
        Assert.Collection(result.Balances,
            b => Assert.Equal(new ParsedBalance("line 2", "EXAMPLE-CHK-1", new DateOnly(2026, 10, 15), 1234.56m, null), b),
            b => Assert.Equal(new ParsedBalance("line 3", "EXAMPLE-CC-1", new DateOnly(2026, 10, 15), -250.00m, null), b));
        Assert.Equal("import:csv", new CsvBalanceSource().SourceSystem);
    }

    [Fact]
    public void Columns_may_come_in_any_order_with_an_optional_USD_currency()
    {
        var result = Parse("currency,balance,account,as_of_date\r\nUSD,10.5,EXAMPLE-SAV-1,2026-10-01\r\n");

        var b = Assert.Single(result.Balances);
        Assert.Equal(("EXAMPLE-SAV-1", new DateOnly(2026, 10, 1), 10.5m), (b.SourceId, b.AsOfDate, b.Amount));
    }

    [Fact]
    public void Reads_a_byte_order_mark_CRLF_and_no_final_newline()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("account,as_of_date,balance\r\nEXAMPLE-CHK-1,2026-10-15,1")).ToArray();

        Assert.Equal(1m, Assert.Single(new CsvBalanceSource().Parse(bytes).Balances).Amount);
    }

    [Fact]
    public void Reads_RFC_4180_quoting()
    {
        var result = Parse("account,as_of_date,balance\n\"EXAMPLE, \"\"JOINT\"\" 1\",2026-10-15,\"5\"\n");

        Assert.Equal("EXAMPLE, \"JOINT\" 1", Assert.Single(result.Balances).SourceId);
    }

    [Theory]
    [InlineData("03/04/2026")]   // March 4 or April 3: refused, not guessed (slice-02.md "Dates and time")
    [InlineData("2026-3-4")]
    [InlineData("2026-02-30")]
    [InlineData("2026-10-15T00:00:00Z")]
    [InlineData("")]
    public void Refuses_a_date_that_is_not_an_ISO_calendar_date(string date)
    {
        var error = OnlyError(Parse($"account,as_of_date,balance\nEXAMPLE-CHK-1,{date},1\n"));

        Assert.Equal("line 2", error.Location);
        Assert.Contains("as_of_date", error.Reason);
    }

    [Theory]
    [InlineData("1,234.56")]   // grouping: also changes the field count, so quoted here
    [InlineData("1234,56")]
    [InlineData("1e3")]
    [InlineData("+5")]
    [InlineData("1.23456")]    // five places: refused, not rounded (R2-M8)
    [InlineData("$5")]
    [InlineData(" 5")]
    public void Refuses_an_amount_the_API_would_refuse(string amount)
    {
        var error = OnlyError(Parse($"account,as_of_date,balance\nEXAMPLE-CHK-1,2026-10-15,\"{amount}\"\n"));

        Assert.Contains("balance", error.Reason);
    }

    [Fact]
    public void Reads_amounts_the_same_on_a_comma_decimal_machine()
    {
        // Spike E: the library it replaced read 1234.56 as 123456 under de-DE.
        var result = Culture.Under("de-DE", () => Parse("account,as_of_date,balance\nEXAMPLE-CHK-1,2026-10-15,1234.56\n"));

        Assert.Equal(1234.56m, Assert.Single(result.Balances).Amount);
    }

    [Fact]
    public void Refuses_a_currency_other_than_USD()
    {
        var error = OnlyError(Parse("account,as_of_date,balance,currency\nEXAMPLE-CHK-1,2026-10-15,1,EUR\n"));

        Assert.Contains("only USD", error.Reason);
    }

    [Theory]
    [InlineData("account,as_of_date,balance,memo", "unknown column 'memo'")]
    [InlineData("account,as_of_date", "missing column 'balance'")]
    [InlineData("account,as_of_date,balance,account", "appears twice")]
    [InlineData("Account,as_of_date,balance", "unknown column 'Account'")]
    public void Refuses_a_header_it_would_not_read_completely(string header, string expected)
    {
        var result = Parse(header + "\nEXAMPLE-CHK-1,2026-10-15,1\n");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Location == "line 1" && e.Reason.Contains(expected));
    }

    [Fact]
    public void Refuses_a_header_with_a_broken_quote()
    {
        var error = OnlyError(Parse("\"account,as_of_date,balance\nEXAMPLE-CHK-1,2026-10-15,1\n"));

        Assert.Equal(("line 1", "header: a quoted field is not closed on this line"), (error.Location, error.Reason));
    }

    [Theory]
    [InlineData("EXAMPLE-CHK-1,2026-10-15", "has 2 fields")]
    [InlineData("\"EXAMPLE-CHK-1,2026-10-15,1", "not closed")]
    [InlineData("EXAMPLE\"CHK,2026-10-15,1", "quote inside")]
    [InlineData("\"EXAMPLE\"X,2026-10-15,1", "follows a closing quote")]
    [InlineData(" EXAMPLE-CHK-1,2026-10-15,1", "whitespace")]
    [InlineData(",2026-10-15,1", "is empty")]
    public void Refuses_a_malformed_row(string row, string expected)
    {
        var error = OnlyError(Parse("account,as_of_date,balance\n" + row + "\n"));

        Assert.Contains(expected, error.Reason);
    }

    [Fact]
    public void Refuses_an_account_id_longer_than_the_limit()
    {
        var error = OnlyError(Parse($"account,as_of_date,balance\n{new string('A', IngestionLimits.MaxSourceIdLength + 1)},2026-10-15,1\n"));

        Assert.Contains("longer than", error.Reason);
    }

    [Fact]
    public void Refuses_a_blank_line_between_rows()
    {
        var error = OnlyError(Parse("account,as_of_date,balance\nEXAMPLE-CHK-1,2026-10-15,1\n\nEXAMPLE-CHK-2,2026-10-15,2\n"));

        Assert.Equal(("line 3", "is blank"), (error.Location, error.Reason));
    }

    [Fact]
    public void Refuses_a_second_balance_for_the_same_account_and_date()
    {
        var error = OnlyError(Parse("account,as_of_date,balance\nEXAMPLE-CHK-1,2026-10-15,1\nEXAMPLE-CHK-1,2026-10-15,2\n"));

        Assert.Equal("line 3", error.Location);
        Assert.Contains("the first is line 2", error.Reason);
    }

    [Fact]
    public void One_bad_line_refuses_the_whole_file_and_every_error_is_reported()
    {
        var result = Parse("account,as_of_date,balance\nEXAMPLE-CHK-1,2026-10-15,1\nEXAMPLE-CHK-2,15/10/2026,2\nEXAMPLE-CHK-3,2026-10-15,x\n");

        Assert.Empty(result.Balances); // all or nothing: the good line 2 is not returned
        Assert.Equal(["line 3", "line 4"], result.Errors.Select(e => e.Location));
    }

    [Fact]
    public void Reports_at_most_the_limit_of_errors_and_says_how_many_more()
    {
        var rows = string.Concat(Enumerable.Range(1, 60).Select(i => $"EXAMPLE-{i},bad,1\n"));

        var result = Parse("account,as_of_date,balance\n" + rows);

        Assert.Equal(IngestionLimits.MaxErrorsReported + 1, result.Errors.Count);
        Assert.Equal("and 10 more errors, not listed", result.Errors[^1].Reason);
    }

    [Fact]
    public void Refuses_more_rows_than_the_limit()
    {
        var rows = string.Concat(Enumerable.Range(1, IngestionLimits.MaxRows + 1).Select(i => $"EXAMPLE-{i},2026-10-15,1\n"));

        var error = OnlyError(Parse("account,as_of_date,balance\n" + rows));

        Assert.Contains($"at most {IngestionLimits.MaxRows}", error.Reason);
    }

    [Fact]
    public void Accepts_exactly_the_row_limit()
    {
        var rows = string.Concat(Enumerable.Range(1, IngestionLimits.MaxRows).Select(i => $"EXAMPLE-{i},2026-10-15,1\n"));

        Assert.Equal(IngestionLimits.MaxRows, Parse("account,as_of_date,balance\n" + rows).Balances.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("account,as_of_date,balance\n")]
    public void Refuses_a_file_with_no_rows(string text) =>
        Assert.Equal("file", OnlyError(Parse(text)).Location);

    [Fact]
    public void Refuses_bytes_that_are_not_UTF8()
    {
        var bytes = Encoding.UTF8.GetBytes("account,as_of_date,balance\nEXAMPLE-").Concat(new byte[] { 0xC3, 0x28 }).ToArray();

        Assert.Equal("is not UTF-8 text", OnlyError(new CsvBalanceSource().Parse(bytes)).Reason);
    }

    [Fact]
    public void Quotes_file_text_in_an_error_bounded_and_with_control_characters_escaped()
    {
        // FR-1.5: a refused value is data. It must not carry a line break or an escape sequence
        // into a log or a page, nor more than a bounded prefix of itself.
        var hostile = "\u001b[31m" + new string('x', 200);

        var error = OnlyError(Parse($"account,as_of_date,balance\nEXAMPLE-CHK-1,2026-10-15,\"{hostile}\"\n"));

        Assert.DoesNotContain('\u001b', error.Reason);
        Assert.Contains("\\u001b", error.Reason);
        Assert.Contains("...'", error.Reason);
        Assert.True(error.Reason.Length < 400, error.Reason);
    }
}
