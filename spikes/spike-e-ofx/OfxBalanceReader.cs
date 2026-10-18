// Spike E: the smallest reader that could work. Reads OFX 1.x (SGML) and 2.x (XML) into one
// element tree, then pulls each statement's LEDGERBAL. Not production code: step 2 rewrites it
// against what RESULTS.md records.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace SpikeE;

sealed class Node(string name)
{
    public string Name { get; } = name;
    public string? Value { get; set; }
    public List<Node> Children { get; } = [];
    public Node? Child(string name) => Children.FirstOrDefault(c => c.Name == name);
    public IEnumerable<Node> Descendants(string name) =>
        Children.SelectMany(c => (c.Name == name ? [c] : Enumerable.Empty<Node>()).Concat(c.Descendants(name)));
}

sealed record Balance(string Kind, string AccountId, DateOnly AsOfDate, decimal Amount, string Currency, string DtAsOfRaw);

static partial class OfxBalanceReader
{
    public static (List<Balance> Balances, List<string> Errors, string Version) Read(byte[] bytes)
    {
        var errors = new List<string>();
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 64)).TrimStart('﻿', ' ', '\r', '\n', '\t');
        Node root;
        string version;
        if (head.StartsWith("<?xml", StringComparison.Ordinal))
            (root, version) = ReadXml(bytes);
        else if (head.StartsWith("OFXHEADER:", StringComparison.Ordinal))
            (root, version) = ReadSgml(bytes);
        else
            throw new FormatException("not OFX: neither an OFXHEADER: line nor an XML declaration");

        var balances = new List<Balance>();
        foreach (var (stmt, from, kind) in new[] { ("STMTRS", "BANKACCTFROM", "bank"), ("CCSTMTRS", "CCACCTFROM", "creditcard") })
            foreach (var rs in root.Descendants(stmt))
            {
                var acct = rs.Child(from)?.Child("ACCTID")?.Value;
                var bal = rs.Child("LEDGERBAL");
                var amt = bal?.Child("BALAMT")?.Value;
                var asOf = bal?.Child("DTASOF")?.Value;
                var cur = rs.Child("CURDEF")?.Value;
                if (acct is null || amt is null || asOf is null || cur is null)
                {
                    errors.Add($"{stmt}: missing ACCTID, LEDGERBAL/BALAMT, LEDGERBAL/DTASOF or CURDEF");
                    continue;
                }
                if (!AmountRx().IsMatch(amt)) { errors.Add($"{stmt} {acct}: BALAMT '{amt}' is not a '.'-decimal"); continue; }
                if (!TryDate(asOf, out var date)) { errors.Add($"{stmt} {acct}: DTASOF '{asOf}' unreadable"); continue; }
                balances.Add(new(kind, acct, date,
                    decimal.Parse(amt, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
                    cur, asOf));
            }
        foreach (var inv in root.Descendants("INVSTMTRS"))
            errors.Add($"INVSTMTRS {inv.Child("INVACCTFROM")?.Child("ACCTID")?.Value}: investment statement - no LEDGERBAL; refused");
        return (balances, errors, version);
    }

    // OFX datetime: YYYYMMDD[HHMMSS[.XXX]][[offset[:TZ]]]. The as-of DATE is the first eight
    // digits as written, i.e. the date in the zone the file states - not converted to UTC.
    static bool TryDate(string s, out DateOnly date)
    {
        var m = DateRx().Match(s);
        date = default;
        return m.Success && DateOnly.TryParseExact(m.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    [GeneratedRegex(@"^(\d{8})(\d{6}(\.\d{3})?)?(\[[+-]?\d+(\.\d+)?(:[A-Za-z]+)?\])?$")] private static partial Regex DateRx();
    [GeneratedRegex(@"^[+-]?\d+(\.\d+)?$")] private static partial Regex AmountRx();
    [GeneratedRegex(@"<(/?)([A-Za-z0-9.]+)>([^<]*)")] private static partial Regex TagRx();

    static (Node, string) ReadSgml(byte[] bytes)
    {
        // Header is ASCII KEY:VALUE lines up to the first '<'. CHARSET says how to decode the body.
        var ascii = Encoding.ASCII.GetString(bytes);
        var bodyAt = ascii.IndexOf('<');
        if (bodyAt < 0) throw new FormatException("no body");
        var header = ascii[..bodyAt].Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(':', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        if (header.GetValueOrDefault("DATA") != "OFXSGML") throw new FormatException("DATA is not OFXSGML");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var enc = (header.GetValueOrDefault("ENCODING"), header.GetValueOrDefault("CHARSET")) switch
        {
            ("UTF-8", _) => Encoding.UTF8,
            (_, "1252") => Encoding.GetEncoding(1252),
            (_, "ISO-8859-1") => Encoding.Latin1,
            ("USASCII", "NONE") => Encoding.ASCII,
            var other => throw new FormatException($"unsupported ENCODING/CHARSET {other}"),
        };
        var body = enc.GetString(bytes)[bodyAt..];

        var root = new Node("#root");
        var stack = new Stack<Node>([root]);
        Node? lastLeaf = null;
        foreach (Match m in TagRx().Matches(body))
        {
            var closing = m.Groups[1].Value == "/";
            var name = m.Groups[2].Value.ToUpperInvariant();
            var text = m.Groups[3].Value.Trim();
            if (closing)
            {
                // An explicitly closed leaf (allowed, not required, in 1.x).
                if (lastLeaf is not null && lastLeaf.Name == name) { lastLeaf = null; continue; }
                if (!stack.Any(n => n.Name == name)) throw new FormatException($"</{name}> closes nothing open");
                while (stack.Peek().Name != name) stack.Pop();
                stack.Pop();
                lastLeaf = null;
                continue;
            }
            var node = new Node(name);
            stack.Peek().Children.Add(node);
            if (text.Length > 0) { node.Value = text; lastLeaf = node; }   // leaf: tag followed by data
            else { stack.Push(node); lastLeaf = null; }                    // aggregate: tag followed by a tag
        }
        if (stack.Count != 1) throw new FormatException($"unclosed aggregate <{stack.Peek().Name}>");
        return (root, "1.x/" + header.GetValueOrDefault("VERSION"));
    }

    static (Node, string) ReadXml(byte[] bytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, // a DOCTYPE is refused outright
            XmlResolver = null,                     // and nothing external is ever fetched
            IgnoreComments = true,
            IgnoreWhitespace = true,
            MaxCharactersInDocument = 1 << 20,
        };
        using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
        var root = new Node("#root");
        var stack = new Stack<Node>([root]);
        var version = "2.x/?";
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.ProcessingInstruction when reader.Name == "OFX":
                    var v = Regex.Match(reader.Value, "VERSION=\"(\\d+)\"");
                    version = "2.x/" + (v.Success ? v.Groups[1].Value : "?");
                    break;
                case XmlNodeType.Element:
                    var node = new Node(reader.Name);
                    stack.Peek().Children.Add(node);
                    if (!reader.IsEmptyElement) stack.Push(node);
                    break;
                case XmlNodeType.Text:
                    stack.Peek().Value = reader.Value.Trim();
                    break;
                case XmlNodeType.EndElement:
                    stack.Pop();
                    break;
            }
        }
        return (root, version);
    }
}
