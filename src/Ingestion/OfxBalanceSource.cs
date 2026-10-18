using System.Globalization;
using System.Text.RegularExpressions;
using Portfolio.Domain;

namespace Portfolio.Ingestion;

/// <summary>
/// The LEDGER balance of each bank (<c>STMTRS</c>) and credit-card (<c>CCSTMTRS</c>) statement
/// in an OFX 1.x or 2.x file (Spike E). The rules Spike E's OfxNet comparison earned:
/// <list type="bullet">
/// <item>amounts are invariant <c>.</c>-decimals through <see cref="MoneyText"/>, never the
/// machine's culture - OfxNet read <c>1234.56</c> as <c>123456</c> on a de-DE machine;</item>
/// <item>any other statement type REFUSES the file - OfxNet returned nothing, silently, for an
/// investment statement, and a file naming an account this cannot read has not been imported;</item>
/// <item><c>DTASOF</c>'s date is taken AS WRITTEN; the full stamp, when it has a time, becomes
/// <see cref="ParsedBalance.SourceAsOf"/> in UTC (slice-02.md "Dates and time");</item>
/// <item>XML is read with DTDs prohibited (<see cref="OfxTree"/>).</item>
/// </list>
/// <c>AVAILBAL</c> is not read: available credit or cash is a different figure.
/// </summary>
public sealed partial class OfxBalanceSource : IBalanceSource
{
    public const string System = "import:ofx";
    public string SourceSystem => System;

    /// <summary>An export from the institution (slice-02.md step 2).</summary>
    public SourceStrength Strength => SourceStrength.Export;

    static readonly (string Statement, string Transaction, string AccountFrom)[] Readable =
    [
        ("STMTRS", "STMTTRNRS", "BANKACCTFROM"),
        ("CCSTMTRS", "CCSTMTTRNRS", "CCACCTFROM"),
    ];

    // YYYYMMDD[HHMMSS[.XXX]][[offset[:TZ]]], offset in hours and possibly fractional (-3.5).
    [GeneratedRegex(@"^(?<date>\d{8})(?:(?<time>\d{6})(?:\.\d{1,3})?)?(?:\[(?<offset>[+-]?\d{1,2}(?:\.\d{1,2})?)(?::[A-Za-z]{1,8})?\])?$")]
    private static partial Regex OfxDateTime();

    public ParseResult Parse(ReadOnlySpan<byte> content)
    {
        OfxNode root;
        try
        {
            (root, _) = OfxTree.Read(content);
        }
        catch (FormatException e)
        {
            return ParseResult.Refused("file", e.Message);
        }

        var errors = new List<ParseError>();
        var all = root.Descendants().ToList();

        foreach (var name in all.Select(n => n.Name).Where(n => n.EndsWith("STMTRS", StringComparison.Ordinal)).Distinct())
            if (!Readable.Any(r => r.Statement == name))
                errors.Add(new("file", $"contains a {name} statement, which this importer does not read; the file is refused rather than imported in part"));

        // A statement whose transaction reports a non-zero STATUS is the institution saying it
        // could not produce it.
        foreach (var (_, transaction, _) in Readable)
            foreach (var trn in all.Where(n => n.Name == transaction))
                if (trn.Leaf("STATUS", "CODE") is { } code && code != "0")
                    errors.Add(new("file", $"a {transaction} reports STATUS CODE {Text.Quote(code)}, not 0"));

        var statements = all
            .Select(n => (Node: n, Kind: Readable.FirstOrDefault(r => r.Statement == n.Name)))
            .Where(x => x.Kind.Statement is not null)
            .ToList();
        if (statements.Count == 0 && errors.Count == 0)
            return ParseResult.Refused("file", "contains no bank or credit-card statement");
        if (statements.Count > IngestionLimits.MaxRows)
            return ParseResult.Refused("file", $"has {statements.Count} statements; at most {IngestionLimits.MaxRows} are accepted");

        var balances = new List<ParsedBalance>();
        var seen = new Dictionary<(string, DateOnly), string>();
        for (var i = 0; i < statements.Count; i++)
        {
            var (node, kind) = statements[i];
            var before = errors.Count;
            var id = node.Leaf(kind.AccountFrom, "ACCTID");
            var where = $"statement {i + 1}" + (id is null ? "" : $" ({Text.Quote(id)})");

            if (id is null) errors.Add(new(where, $"has no {kind.AccountFrom}/ACCTID"));
            else if (Text.SourceIdProblem(id) is { } idProblem) errors.Add(new(where, $"ACCTID {idProblem}"));

            var currency = node.Leaf("CURDEF");
            if (currency != ReportingCurrency.Code)
                errors.Add(new(where, $"CURDEF {Text.Quote(currency)}: only {ReportingCurrency.Code} is accepted (single currency, M-11)"));

            var amountText = node.Leaf("LEDGERBAL", "BALAMT");
            decimal amount = 0;
            if (amountText is null) errors.Add(new(where, "has no LEDGERBAL/BALAMT"));
            else if (!MoneyText.TryParse(amountText.StartsWith('+') ? amountText[1..] : amountText, out amount, out var moneyError))
                errors.Add(new(where, $"BALAMT {Text.Quote(amountText)}: {moneyError}"));

            var stamp = node.Leaf("LEDGERBAL", "DTASOF");
            DateOnly date = default;
            DateTimeOffset? sourceAsOf = null;
            if (stamp is null) errors.Add(new(where, "has no LEDGERBAL/DTASOF"));
            else if (!TryReadStamp(stamp, out date, out sourceAsOf, out var stampError))
                errors.Add(new(where, $"DTASOF {Text.Quote(stamp)}: {stampError}"));

            if (errors.Count > before) continue;
            if (seen.TryGetValue((id!, date), out var first))
            {
                errors.Add(new(where, $"a second balance for {Text.Quote(id)} on {date:yyyy-MM-dd} (the first is {first}); which one is meant cannot be told"));
                continue;
            }
            seen[(id!, date)] = where;
            balances.Add(new(where, id!, date, amount, sourceAsOf));
        }
        return ParseResult.From(balances, errors);
    }

    /// <summary>
    /// The date is the first eight digits AS WRITTEN - the day the institution meant, in the zone
    /// it states. Converting <c>23:00 [-5]</c> to UTC first would move it to the next day. When a
    /// time is present the whole stamp is also kept, as a UTC moment, for provenance. No offset
    /// means UTC [INFERRED: the OFX specification's default as recalled, not read].
    /// </summary>
    internal static bool TryReadStamp(string stamp, out DateOnly date, out DateTimeOffset? moment, out string? error)
    {
        date = default;
        moment = null;
        error = null;
        var m = OfxDateTime().Match(stamp);
        if (!m.Success)
        {
            error = "is not an OFX date (YYYYMMDD, optionally HHMMSS[.XXX] and [offset:TZ])";
            return false;
        }
        if (!DateOnly.TryParseExact(m.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            error = "is not a calendar date";
            return false;
        }
        if (!m.Groups["time"].Success) return true;

        if (!TimeOnly.TryParseExact(m.Groups["time"].Value, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            error = "has a time that is not a clock time";
            return false;
        }
        var hours = m.Groups["offset"].Success
            ? decimal.Parse(m.Groups["offset"].Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
            : 0m;
        if (hours is < -12 or > 14)
        {
            error = $"offset {hours} hours is outside -12..+14";
            return false;
        }
        if (hours * 60 != decimal.Truncate(hours * 60))
        {
            error = $"offset {hours} hours is not a whole number of minutes";
            return false;
        }
        var offset = TimeSpan.FromMinutes((double)(hours * 60));
        moment = new DateTimeOffset(date.ToDateTime(time), offset).ToUniversalTime();
        return true;
    }
}
