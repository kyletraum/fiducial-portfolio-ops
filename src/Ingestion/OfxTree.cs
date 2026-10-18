using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Portfolio.Ingestion;

/// <summary>One OFX element: an aggregate (children) or a leaf (a value).</summary>
sealed class OfxNode(string name)
{
    public string Name { get; } = name;
    public string? Value { get; set; }
    public List<OfxNode> Children { get; } = [];

    public OfxNode? Child(string name) => Children.FirstOrDefault(c => c.Name == name);
    public string? Leaf(params string[] path)
    {
        var node = this;
        foreach (var step in path)
        {
            var next = node.Child(step);
            if (next is null) return null;
            node = next;
        }
        return node.Value;
    }

    public IEnumerable<OfxNode> Descendants() => Children.SelectMany(c => new[] { c }.Concat(c.Descendants()));
}

/// <summary>
/// Reads OFX 1.x (SGML, leaf elements' closing tags optional) and OFX 2.x (XML) into ONE
/// element tree, so everything after this point sees no difference (Spike E,
/// spikes/spike-e-ofx/RESULTS.md). Throws <see cref="FormatException"/> for anything it will
/// not read.
/// </summary>
static partial class OfxTree
{
    public static (OfxNode Root, string Version) Read(ReadOnlySpan<byte> bytes)
    {
        // A UTF-8 byte-order mark. Bytes, not a "\xEF..."u8 literal: in a UTF-8 string literal
        // \xEF is the CHARACTER U+00EF, two bytes, and the check would never match.
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) bytes = bytes[3..];
        var start = Encoding.ASCII.GetString(bytes[..Math.Min(bytes.Length, 256)]).TrimStart(' ', '\t', '\r', '\n');
        if (start.StartsWith("<?xml", StringComparison.Ordinal)) return ReadXml(bytes);
        if (start.StartsWith("OFXHEADER:", StringComparison.Ordinal)) return ReadSgml(bytes);
        throw new FormatException("is not OFX: it starts with neither an OFXHEADER: line (1.x) nor an XML declaration (2.x)");
    }

    [GeneratedRegex(@"<(/?)([A-Za-z0-9.]+)>([^<]*)")]
    private static partial Regex Tag();

    static (OfxNode, string) ReadSgml(ReadOnlySpan<byte> bytes)
    {
        var ascii = Encoding.ASCII.GetString(bytes);
        var bodyAt = ascii.IndexOf('<');
        if (bodyAt < 0) throw new FormatException("has an OFX header and no body");
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in ascii[..bodyAt].Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2) header[parts[0].Trim()] = parts[1].Trim();
        }
        if (header.GetValueOrDefault("DATA") != "OFXSGML")
            throw new FormatException($"header DATA is {Text.Quote(header.GetValueOrDefault("DATA"))}, not OFXSGML");
        var version = header.GetValueOrDefault("VERSION") ?? "";
        if (!version.StartsWith('1')) throw new FormatException($"header VERSION {Text.Quote(version)} is not an OFX 1.x version");

        // CHARSET says how the body's bytes are text. Code page 1252 ships in .NET 10's shared
        // framework but must be registered (Spike E).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = (header.GetValueOrDefault("ENCODING"), header.GetValueOrDefault("CHARSET")) switch
        {
            ("UTF-8", _) => Encoding.UTF8,
            ("USASCII", "1252") => Encoding.GetEncoding(1252),
            ("USASCII", "ISO-8859-1") => Encoding.Latin1,
            ("USASCII", "NONE") => Encoding.ASCII,
            var (e, c) => throw new FormatException($"ENCODING {Text.Quote(e)} with CHARSET {Text.Quote(c)} is not supported"),
        };
        var body = encoding.GetString(bytes)[bodyAt..];

        var root = new OfxNode("#root");
        var open = new Stack<OfxNode>([root]);
        OfxNode? lastLeaf = null;
        foreach (Match m in Tag().Matches(body))
        {
            var closing = m.Groups[1].Value == "/";
            var name = m.Groups[2].Value.ToUpperInvariant();
            var text = m.Groups[3].Value.Trim();
            if (closing)
            {
                // A leaf may be closed explicitly, or not (both are 1.x).
                if (lastLeaf is not null && lastLeaf.Name == name) { lastLeaf = null; continue; }
                if (!open.Any(n => n.Name == name)) throw new FormatException($"</{name}> closes nothing that is open");
                if (open.Peek().Name != name) throw new FormatException($"</{name}> closes over <{open.Peek().Name}>, which is still open");
                open.Pop();
                lastLeaf = null;
                continue;
            }
            var node = new OfxNode(name);
            open.Peek().Children.Add(node);
            if (text.Length > 0) { node.Value = Unescape(text); lastLeaf = node; }
            else { open.Push(node); lastLeaf = null; }
        }
        if (open.Count != 1) throw new FormatException($"<{open.Peek().Name}> is never closed");
        return (root, "1.x/" + version);
    }

    /// <summary>The three entities OFX 1.x text uses. Anything else is left as written.</summary>
    static string Unescape(string text) =>
        text.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

    static (OfxNode, string) ReadXml(ReadOnlySpan<byte> bytes)
    {
        var settings = new XmlReaderSettings
        {
            // Spike E: a DOCTYPE refuses the file - OfxNet processed DTDs and expanded an
            // internal entity into the account id. Nothing external is ever fetched.
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            MaxCharactersInDocument = 4_000_000,
        };
        var root = new OfxNode("#root");
        var open = new Stack<OfxNode>([root]);
        string? version = null;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(bytes.ToArray()), settings);
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.ProcessingInstruction when reader.Name == "OFX":
                        var v = Regex.Match(reader.Value, "VERSION=\"(\\d+)\"");
                        version = v.Success ? v.Groups[1].Value : "";
                        break;
                    case XmlNodeType.Element:
                        var node = new OfxNode(reader.Name);
                        open.Peek().Children.Add(node);
                        if (!reader.IsEmptyElement) open.Push(node);
                        break;
                    case XmlNodeType.Text:
                        open.Peek().Value = reader.Value.Trim();
                        break;
                    case XmlNodeType.EndElement:
                        open.Pop();
                        break;
                }
            }
        }
        catch (XmlException e)
        {
            throw new FormatException($"is not readable OFX 2.x XML: {e.Message}");
        }
        if (version is null) throw new FormatException("has an XML declaration but no <?OFX ...?> header");
        if (!version.StartsWith('2')) throw new FormatException($"OFX header VERSION {Text.Quote(version)} is not an OFX 2.x version");
        return (root, "2.x/" + version);
    }
}
