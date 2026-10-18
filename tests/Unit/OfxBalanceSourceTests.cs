using System.Text;
using Portfolio.Ingestion;
using static Portfolio.Unit.OfxFile;

namespace Portfolio.Unit;

/// <summary>
/// slice-02.md step 1, with the rules Spike E earned (spikes/spike-e-ofx/RESULTS.md) and the
/// parser-level cases of DoD 10. Every file is built by <see cref="OfxFile"/>; invented values only.
/// </summary>
public class OfxBalanceSourceTests
{
    static readonly OfxBalanceSource Source = new();

    static string Bank15 => Stamp("2026-10-15", "120000", ".000[-5:EST]");

    static ParseError OnlyError(ParseResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Empty(result.Balances);
        return Assert.Single(result.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reads_OFX_1x_SGML_with_leaf_closing_tags_present_or_absent(bool closeLeaves)
    {
        var file = new OfxFile { CloseLeaves = closeLeaves }
            .Add(Kind.Bank, "EXAMPLE-CHK-1", "1234.56", Bank15)
            .Add(Kind.CreditCard, "EXAMPLE-CC-1", "-250.00", D("2026-10-16"));

        var result = Source.Parse(file.Sgml());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Collection(result.Balances,
            b => Assert.Equal(("EXAMPLE-CHK-1", new DateOnly(2026, 10, 15), 1234.56m), (b.SourceId, b.AsOfDate, b.Amount)),
            b => Assert.Equal(("EXAMPLE-CC-1", new DateOnly(2026, 10, 16), -250.00m), (b.SourceId, b.AsOfDate, b.Amount)));
        Assert.Equal("import:ofx", Source.SourceSystem);
    }

    [Fact]
    public void Reads_OFX_2x_XML_into_the_same_balances()
    {
        var file = new OfxFile().Add(Kind.Bank, "EXAMPLE-SAV-1", "5000.0000", Stamp("2026-10-17", "000000", "[0:GMT]"));

        var b = Assert.Single(Source.Parse(file.Xml()).Balances);

        Assert.Equal(("EXAMPLE-SAV-1", new DateOnly(2026, 10, 17), 5000m), (b.SourceId, b.AsOfDate, b.Amount));
    }

    [Fact]
    public void Reads_a_file_behind_a_UTF8_byte_order_mark()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(new OfxFile().Add(Kind.Bank, "EXAMPLE-CHK-1", "1", Bank15).Xml()).ToArray();

        Assert.True(Source.Parse(bytes).Succeeded);
    }

    // ---- Dates and time (slice-02.md; DoD 10's parser cases) ----

    [Theory]
    // stamp parts                                  date as written   source_as_of (UTC)
    [InlineData("2026-10-15", "235900", "[-5:EST]", "2026-10-15", "2026-10-16T04:59:00Z")] // 23:59 at -5: still the 15th
    [InlineData("2026-10-15", "230000", "[-5:CDT]", "2026-10-15", "2026-10-16T04:00:00Z")] // Kyle's example
    [InlineData("2026-10-15", "010000", "[+14:LINT]", "2026-10-15", "2026-10-14T11:00:00Z")] // +14: the moment is the day before
    [InlineData("2026-10-15", "120000", "[0:GMT]", "2026-10-15", "2026-10-15T12:00:00Z")]
    [InlineData("2026-10-15", "120000", "[+0:GMT]", "2026-10-15", "2026-10-15T12:00:00Z")] // OfxNet threw on [0:GMT]; both read the same
    [InlineData("2026-10-15", "120000", "", "2026-10-15", "2026-10-15T12:00:00Z")]          // no offset: UTC [INFERRED]
    [InlineData("2026-10-15", "120000", "[-3.5:NST]", "2026-10-15", "2026-10-15T15:30:00Z")]
    [InlineData("2026-10-15", "120000", ".123[-5:EST]", "2026-10-15", "2026-10-15T17:00:00Z")] // milliseconds dropped from the moment's minute
    public void Takes_the_date_as_written_and_keeps_the_stamp_as_a_UTC_moment(string iso, string time, string bracket, string expectedDate, string expectedUtc)
    {
        var file = new OfxFile().Add(Kind.Bank, "EXAMPLE-CHK-1", "1", Stamp(iso, time, bracket));

        var b = Assert.Single(Source.Parse(file.Sgml()).Balances);

        Assert.Equal(DateOnly.Parse(expectedDate), b.AsOfDate);
        Assert.Equal(DateTimeOffset.Parse(expectedUtc), b.SourceAsOf);
        Assert.Equal(TimeSpan.Zero, b.SourceAsOf!.Value.Offset);
    }

    [Fact]
    public void A_date_only_stamp_has_no_moment()
    {
        var b = Assert.Single(Source.Parse(new OfxFile().Add(Kind.CreditCard, "EXAMPLE-CC-1", "1", D("2026-10-16")).Sgml()).Balances);

        Assert.Equal(new DateOnly(2026, 10, 16), b.AsOfDate);
        Assert.Null(b.SourceAsOf);
    }

    [Theory]
    [InlineData("2026-02-30", "120000", "")]        // not a calendar date
    [InlineData("2026-10-15", "250000", "")]        // not a clock time
    [InlineData("2026-10-15", "120000", "[+15:X]")] // outside -12..+14
    [InlineData("2026-10-15", "120000", "[5.33:X]")] // not whole minutes
    [InlineData("2026-10-15", "12", "")]            // half a time
    public void Refuses_a_stamp_it_cannot_read_exactly(string iso, string time, string bracket)
    {
        var error = OnlyError(Source.Parse(new OfxFile().Add(Kind.Bank, "EXAMPLE-CHK-1", "1", Stamp(iso, time, bracket)).Sgml()));

        Assert.Contains("DTASOF", error.Reason);
    }

    // ---- Amounts ----

    [Fact]
    public void Refuses_a_comma_decimal_rather_than_reading_it_100_times_too_large()
    {
        // Spike E: OfxNet read 1234,56 as 123456, silently.
        var error = OnlyError(Source.Parse(new OfxFile().Add(Kind.Bank, "EXAMPLE-CHK-1", "1234,56", Bank15).Sgml()));

        Assert.Contains("BALAMT", error.Reason);
    }

    [Fact]
    public void Reads_amounts_the_same_on_a_comma_decimal_machine()
    {
        // Spike E: OfxNet read 1234.56 as 123456 and -250.00 as -25000 under de-DE.
        var file = new OfxFile().Add(Kind.Bank, "EXAMPLE-CHK-1", "1234.56", Bank15).Add(Kind.CreditCard, "EXAMPLE-CC-1", "-250.00", D("2026-10-16"));

        var result = Culture.Under("de-DE", () => Source.Parse(file.Sgml()));

        Assert.Equal([1234.56m, -250.00m], result.Balances.Select(b => b.Amount));
    }

    [Fact]
    public void Accepts_a_leading_plus_sign() =>
        Assert.Equal(12.5m, Assert.Single(Source.Parse(new OfxFile().Add(Kind.Bank, "EXAMPLE-CHK-1", "+12.5", Bank15).Sgml()).Balances).Amount);

    // ---- What refuses the whole file ----

    [Fact]
    public void An_investment_statement_refuses_the_whole_file()
    {
        // Spike E: OfxNet returned nothing, without an error, for an investment statement. A file
        // naming an account this cannot read has not been imported - so the bank statement
        // beside it is not returned either.
        var file = new OfxFile()
            .Add(Kind.Bank, "EXAMPLE-SAV-1", "5000", Bank15)
            .Add(Kind.Investment, "EXAMPLE-INV-1", "10.00", Stamp("2026-10-17", "000000"));

        var error = OnlyError(Source.Parse(file.Xml()));

        Assert.Contains("INVSTMTRS", error.Reason);
    }

    [Theory]
    [InlineData("<!DOCTYPE OFX [<!ENTITY y \"EXPANDED\">]>")]
    [InlineData("<!DOCTYPE OFX [<!ENTITY z SYSTEM \"file:///etc/hostname\">]>")]
    public void A_DOCTYPE_refuses_the_file_before_any_entity_is_read(string doctype)
    {
        // Spike E: OfxNet expanded an internal entity into the account id.
        var file = new OfxFile { Doctype = doctype }.Add(Kind.Bank, "EXAMPLE-SAV-1", "5000", Bank15);

        var error = OnlyError(Source.Parse(file.Xml()));

        Assert.Equal("file", error.Location);
        Assert.Contains("DTD", error.Reason);
    }

    [Fact]
    public void Refuses_a_currency_other_than_USD() =>
        Assert.Contains("only USD", OnlyError(Source.Parse(new OfxFile().Add(Kind.Bank, "EXAMPLE-CHK-1", "1", Bank15, currency: "CAD").Sgml())).Reason);

    [Fact]
    public void Refuses_a_statement_whose_status_is_not_zero() =>
        Assert.Contains("STATUS CODE '2000'", OnlyError(Source.Parse(new OfxFile().Add(Kind.Bank, "EXAMPLE-CHK-1", "1", Bank15, statusCode: "2000").Xml())).Reason);

    [Fact]
    public void Refuses_a_second_balance_for_the_same_account_and_date()
    {
        var file = new OfxFile()
            .Add(Kind.Bank, "EXAMPLE-CHK-1", "1", Bank15)
            .Add(Kind.Bank, "EXAMPLE-CHK-1", "2", Stamp("2026-10-15", "180000", "[-5:EST]"));

        Assert.Contains("the first is statement 1", OnlyError(Source.Parse(file.Sgml())).Reason);
    }

    [Fact]
    public void Refuses_a_file_with_no_statement() =>
        Assert.Contains("no bank or credit-card statement", OnlyError(Source.Parse(new OfxFile().Sgml())).Reason);

    [Theory]
    [InlineData("account,as_of_date,balance\n")]
    [InlineData("")]
    [InlineData("<html></html>")]
    public void Refuses_what_is_not_OFX(string text) =>
        Assert.Contains("is not OFX", OnlyError(Source.Parse(Encoding.UTF8.GetBytes(text))).Reason);

    [Fact]
    public void Refuses_a_header_version_that_does_not_match_its_format()
    {
        Assert.Contains("not an OFX 1.x", OnlyError(Source.Parse(new OfxFile { SgmlVersion = "220" }.Add(Kind.Bank, "EXAMPLE-CHK-1", "1", Bank15).Sgml())).Reason);
        Assert.Contains("not an OFX 2.x", OnlyError(Source.Parse(new OfxFile { XmlVersion = "102" }.Add(Kind.Bank, "EXAMPLE-CHK-1", "1", Bank15).Xml())).Reason);
    }

    [Theory]
    [InlineData("USASCII", "1252")]
    [InlineData("USASCII", "ISO-8859-1")]
    [InlineData("USASCII", "NONE")]
    [InlineData("UTF-8", "NONE")]
    public void Reads_each_1x_encoding_it_supports(string encoding, string charset) =>
        Assert.True(Source.Parse(new OfxFile { Encoding = encoding, Charset = charset }.Add(Kind.Bank, "EXAMPLE-CHK-1", "1", Bank15).Sgml()).Succeeded);

    [Fact]
    public void Refuses_a_1x_header_that_is_not_SGML_or_has_no_body()
    {
        Assert.Contains("not OFXSGML", OnlyError(Source.Parse(Encoding.ASCII.GetBytes("OFXHEADER:100\r\nDATA:OFXXML\r\n\r\n<OFX></OFX>"))).Reason);
        Assert.Contains("no body", OnlyError(Source.Parse(Encoding.ASCII.GetBytes("OFXHEADER:100\r\nDATA:OFXSGML\r\n"))).Reason);
    }

    [Fact]
    public void Refuses_an_encoding_it_does_not_know() =>
        Assert.Contains("is not supported", OnlyError(Source.Parse(new OfxFile { Charset = "8859-15" }.Add(Kind.Bank, "EXAMPLE-CHK-1", "1", Bank15).Sgml())).Reason);

    [Theory]
    [InlineData("</STMTRS>", "</BANKACCTFROM></STMTRS>")] // an aggregate closed over another still open
    [InlineData("</OFX>", "")]                           // never closed
    public void Refuses_SGML_whose_aggregates_do_not_nest(string find, string replace)
    {
        var text = Encoding.Latin1.GetString(new OfxFile().Add(Kind.Bank, "EXAMPLE-CHK-1", "1", Bank15).Sgml());
        var broken = find == "</STMTRS>"
            ? text.Replace("</BANKACCTFROM>", "") // BANKACCTFROM left open when STMTRS closes
            : text.Replace(find, replace);

        Assert.Equal("file", OnlyError(Source.Parse(Encoding.Latin1.GetBytes(broken))).Location);
    }

    [Fact]
    public void Decodes_the_SGML_entities_in_text()
    {
        var b = Assert.Single(Source.Parse(new OfxFile().Add(Kind.Bank, "EXAMPLE&amp;JOINT", "1", Bank15).Sgml()).Balances);

        Assert.Equal("EXAMPLE&JOINT", b.SourceId);
    }

    [Fact]
    public void Does_not_read_AVAILBAL_as_the_balance()
    {
        // The builder writes AVAILBAL 1.00 beside every LEDGERBAL.
        Assert.Equal(777m, Assert.Single(Source.Parse(new OfxFile().Add(Kind.Bank, "EXAMPLE-CHK-1", "777", Bank15).Sgml()).Balances).Amount);
    }
}
