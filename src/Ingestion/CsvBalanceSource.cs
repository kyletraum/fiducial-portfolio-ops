using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Portfolio.Domain;

namespace Portfolio.Ingestion;

/// <summary>
/// This platform's OWN balance format, not an institution's (slice-02.md step 1):
/// <code>
/// account,as_of_date,balance[,currency]
/// EXAMPLE-CHK-1,2026-10-15,1234.56
/// </code>
/// Columns in any order, each exactly once. Strict UTF-8. Fields may be quoted as RFC 4180
/// does it ("..." with "" for a quote), but never across lines. Everything else is REFUSED,
/// not guessed: an unknown column (an unread column is a silent drop), a date that is not ISO
/// (03/04/2026 is March 4 or April 3 depending on who wrote it), an amount <see cref="MoneyText"/>
/// would refuse from the API, a currency other than USD (M-11), a blank line, and a second row
/// for the same account and date.
/// </summary>
public sealed partial class CsvBalanceSource : IBalanceSource
{
    public const string System = "import:csv";
    public string SourceSystem => System;

    /// <summary>An export from the institution (slice-02.md step 2).</summary>
    public SourceStrength Strength => SourceStrength.Export;

    const string Account = "account", AsOfDate = "as_of_date", Balance = "balance", Currency = "currency";
    static readonly string[] Required = [Account, AsOfDate, Balance];
    static readonly string[] Known = [Account, AsOfDate, Balance, Currency];

    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoDate();

    public ParseResult Parse(ReadOnlySpan<byte> content)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return ParseResult.Refused("file", "is not UTF-8 text");
        }
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var lines = text.Split('\n').Select(l => l.EndsWith('\r') ? l[..^1] : l).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1); // the final newline
        if (lines.Count == 0) return ParseResult.Refused("file", "is empty");
        if (lines.Count == 1) return ParseResult.Refused("file", "has a header and no rows");
        if (lines.Count - 1 > IngestionLimits.MaxRows)
            return ParseResult.Refused("file", $"has {lines.Count - 1} rows; at most {IngestionLimits.MaxRows} are accepted");

        if (!TrySplit(lines[0], out var header, out var headerProblem))
            return ParseResult.Refused("line 1", $"header: {headerProblem}");
        var columns = new Dictionary<string, int>();
        var errors = new List<ParseError>();
        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i];
            if (!Known.Contains(name))
                errors.Add(new("line 1", $"unknown column {Text.Quote(name)}; the columns are {string.Join(", ", Known)} (currency optional)"));
            else if (!columns.TryAdd(name, i))
                errors.Add(new("line 1", $"column '{name}' appears twice"));
        }
        foreach (var name in Required.Where(n => !columns.ContainsKey(n)))
            errors.Add(new("line 1", $"missing column '{name}'"));
        if (errors.Count > 0) return ParseResult.From([], errors);

        var balances = new List<ParsedBalance>();
        var seen = new Dictionary<(string, DateOnly), int>();
        for (var n = 2; n <= lines.Count; n++)
        {
            var where = $"line {n}";
            var line = lines[n - 1];
            if (line.Length == 0) { errors.Add(new(where, "is blank")); continue; }
            if (!TrySplit(line, out var fields, out var problem)) { errors.Add(new(where, problem!)); continue; }
            if (fields.Count != header.Count)
            {
                errors.Add(new(where, $"has {fields.Count} fields; the header has {header.Count}"));
                continue;
            }

            var before = errors.Count;
            var id = fields[columns[Account]];
            if (Text.SourceIdProblem(id) is { } idProblem)
                errors.Add(new(where, $"account {Text.Quote(id)} {idProblem}"));

            var dateText = fields[columns[AsOfDate]];
            DateOnly date = default;
            if (!IsoDate().IsMatch(dateText) ||
                !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                errors.Add(new(where, $"as_of_date {Text.Quote(dateText)} is not an ISO date (yyyy-mm-dd); other forms are refused, not guessed"));

            var amountText = fields[columns[Balance]];
            if (!MoneyText.TryParse(amountText, out var amount, out var moneyError))
                errors.Add(new(where, $"balance {Text.Quote(amountText)}: {moneyError}"));

            if (columns.TryGetValue(Currency, out var c) && fields[c] != ReportingCurrency.Code)
                errors.Add(new(where, $"currency {Text.Quote(fields[c])}: only {ReportingCurrency.Code} is accepted (single currency, M-11)"));

            if (errors.Count > before) continue;
            if (seen.TryGetValue((id, date), out var first))
            {
                errors.Add(new(where, $"a second balance for {Text.Quote(id)} on {date:yyyy-MM-dd} (the first is line {first}); which one is meant cannot be told"));
                continue;
            }
            seen[(id, date)] = n;
            balances.Add(new(where, id, date, amount, SourceAsOf: null));
        }
        return ParseResult.From(balances, errors);
    }

    /// <summary>RFC 4180 fields within one line. A quote that is not closed on the line is refused.</summary>
    static bool TrySplit(string line, out List<string> fields, out string? problem)
    {
        fields = [];
        problem = null;
        var i = 0;
        while (true)
        {
            var field = new StringBuilder();
            if (i < line.Length && line[i] == '"')
            {
                i++;
                while (true)
                {
                    if (i >= line.Length) { problem = "a quoted field is not closed on this line"; return false; }
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                        i++;
                        break;
                    }
                    field.Append(line[i++]);
                }
                if (i < line.Length && line[i] != ',') { problem = "text follows a closing quote"; return false; }
            }
            else
            {
                while (i < line.Length && line[i] != ',')
                {
                    if (line[i] == '"') { problem = "a quote inside an unquoted field"; return false; }
                    field.Append(line[i++]);
                }
            }
            fields.Add(field.ToString());
            if (i >= line.Length) return true;
            i++; // the comma
        }
    }
}
