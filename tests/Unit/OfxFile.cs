using System.Text;

namespace Portfolio.Unit;

/// <summary>
/// Invented OFX files, built in code and never committed (slice-02.md step 5): a committed
/// .ofx would carry fourteen-digit DTASOF stamps that the hygiene scanner's account-number rule
/// refuses on sight, and no file in the repository should look like an export. Every stamp is
/// built from an ISO date for the same reason - this source file carries no eight-digit run.
/// Constitution IX: every value is made up.
/// </summary>
sealed class OfxFile
{
    public enum Kind { Bank, CreditCard, Investment }

    public sealed record Statement(Kind Kind, string AccountId, string Amount, string DtAsOf,
        string Currency = "USD", string? StatusCode = "0");

    readonly List<Statement> statements = [];
    public bool CloseLeaves { get; init; }
    public string Charset { get; init; } = "1252";
    public string Encoding { get; init; } = "USASCII";
    public string SgmlVersion { get; init; } = "102";
    public string XmlVersion { get; init; } = "220";
    public string? Doctype { get; init; }

    public OfxFile Add(Kind kind, string accountId, string amount, string dtAsOf, string currency = "USD", string? statusCode = "0")
    {
        statements.Add(new(kind, accountId, amount, dtAsOf, currency, statusCode));
        return this;
    }

    /// <summary>"2026-10-15" -> the eight OFX date digits.</summary>
    public static string D(string isoDate) => isoDate.Replace("-", "");

    /// <summary>An OFX stamp from an ISO date, a six-digit time and an optional bracket, e.g. "[-5:EST]".</summary>
    public static string Stamp(string isoDate, string time, string bracket = "") => D(isoDate) + time + bracket;

    public byte[] Sgml()
    {
        string L(string tag, string value) => CloseLeaves ? $"<{tag}>{value}</{tag}>" : $"<{tag}>{value}";
        var sb = new StringBuilder();
        foreach (var h in new[] { "OFXHEADER:100", "DATA:OFXSGML", $"VERSION:{SgmlVersion}", "SECURITY:NONE",
                     $"ENCODING:{Encoding}", $"CHARSET:{Charset}", "COMPRESSION:NONE", "OLDFILEUID:NONE", "NEWFILEUID:NONE", "" })
            sb.Append(h).Append("\r\n");
        sb.Append("<OFX>\r\n<SIGNONMSGSRSV1><SONRS><STATUS>").Append(L("CODE", "0")).Append(L("SEVERITY", "INFO"))
          .Append("</STATUS>").Append(L("DTSERVER", Stamp("2026-10-18", "120000"))).Append(L("LANGUAGE", "ENG"))
          .Append("</SONRS></SIGNONMSGSRSV1>\r\n");
        foreach (var s in statements) sb.Append(StatementText(s, L)).Append("\r\n");
        sb.Append("</OFX>\r\n");
        return System.Text.Encoding.Latin1.GetBytes(sb.ToString());
    }

    public byte[] Xml()
    {
        string L(string tag, string value) => $"<{tag}>{value}</{tag}>";
        var sb = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n")
            .Append($"<?OFX OFXHEADER=\"200\" VERSION=\"{XmlVersion}\" SECURITY=\"NONE\" OLDFILEUID=\"NONE\" NEWFILEUID=\"NONE\"?>\n")
            .Append(Doctype is null ? "" : Doctype + "\n")
            .Append("<OFX>\n<SIGNONMSGSRSV1><SONRS><STATUS><CODE>0</CODE><SEVERITY>INFO</SEVERITY></STATUS>")
            .Append(L("DTSERVER", Stamp("2026-10-18", "120000"))).Append("<LANGUAGE>ENG</LANGUAGE></SONRS></SIGNONMSGSRSV1>\n");
        foreach (var s in statements) sb.Append(StatementText(s, L)).Append('\n');
        sb.Append("</OFX>\n");
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    static string StatementText(Statement s, Func<string, string, string> L)
    {
        var status = s.StatusCode is null ? "" : "<STATUS>" + L("CODE", s.StatusCode) + L("SEVERITY", "INFO") + "</STATUS>";
        return s.Kind switch
        {
            Kind.Bank =>
                "<BANKMSGSRSV1><STMTTRNRS>" + L("TRNUID", "1") + status + "<STMTRS>" + L("CURDEF", s.Currency)
                + "<BANKACCTFROM>" + L("BANKID", new string('1', 9)) + L("ACCTID", s.AccountId) + L("ACCTTYPE", "CHECKING") + "</BANKACCTFROM>"
                + "<LEDGERBAL>" + L("BALAMT", s.Amount) + L("DTASOF", s.DtAsOf) + "</LEDGERBAL>"
                + "<AVAILBAL>" + L("BALAMT", "1.00") + L("DTASOF", s.DtAsOf) + "</AVAILBAL>"
                + "</STMTRS></STMTTRNRS></BANKMSGSRSV1>",
            Kind.CreditCard =>
                "<CREDITCARDMSGSRSV1><CCSTMTTRNRS>" + L("TRNUID", "2") + status + "<CCSTMTRS>" + L("CURDEF", s.Currency)
                + "<CCACCTFROM>" + L("ACCTID", s.AccountId) + "</CCACCTFROM>"
                + "<LEDGERBAL>" + L("BALAMT", s.Amount) + L("DTASOF", s.DtAsOf) + "</LEDGERBAL>"
                + "</CCSTMTRS></CCSTMTTRNRS></CREDITCARDMSGSRSV1>",
            _ =>
                "<INVSTMTMSGSRSV1><INVSTMTTRNRS>" + L("TRNUID", "3") + status + "<INVSTMTRS>" + L("DTASOF", s.DtAsOf) + L("CURDEF", s.Currency)
                + "<INVACCTFROM>" + L("BROKERID", "example.invalid") + L("ACCTID", s.AccountId) + "</INVACCTFROM>"
                + "<INVBAL>" + L("AVAILCASH", s.Amount) + "</INVBAL>"
                + "</INVSTMTRS></INVSTMTTRNRS></INVSTMTMSGSRSV1>",
        };
    }
}

/// <summary>Runs a body under another culture and puts the old one back.</summary>
static class Culture
{
    public static T Under<T>(string name, Func<T> body)
    {
        var (culture, ui) = (System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CultureInfo.CurrentUICulture);
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new(name);
            System.Globalization.CultureInfo.CurrentUICulture = new(name);
            return body();
        }
        finally
        {
            (System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CultureInfo.CurrentUICulture) = (culture, ui);
        }
    }
}
