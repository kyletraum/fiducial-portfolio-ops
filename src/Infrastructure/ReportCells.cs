using System.Buffers.Binary;
using System.Collections;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace Portfolio.Infrastructure;

/// <summary>
/// slice-04.md "Results of unknown shape" (C4-12, SCO-5, SRC-9): each cell read by its PostgreSQL
/// type, and counted against the byte budget as it is read (R2-TST-6).
/// </summary>
/// <remarks>
/// <para>The batch path has no text-format switch (SRC-9: <c>AllResultTypesAreUnknown</c> is on
/// <see cref="NpgsqlCommand"/> only), so the binary values are read by type:</para>
/// <list type="bullet">
/// <item>text, <c>varchar</c>, <c>char(n)</c>, <c>name</c>, <c>json</c>, <c>jsonb</c>, <c>xml</c>: a string,
/// read in chunks through a <see cref="TextReader"/>, so a 100 MB cell is never whole in memory;</item>
/// <item><c>bytea</c>: <c>\x</c> and hex, as PostgreSQL writes it, read in chunks;</item>
/// <item><c>numeric</c>: a string with every digit, decoded from the wire form, so neither
/// <c>decimal</c>'s range nor <c>NaN</c> fails it (money keeps every digit);</item>
/// <item><c>boolean</c>, <c>smallint</c>, <c>integer</c>, and finite <c>real</c> and <c>double precision</c>:
/// JSON booleans and numbers, which a browser holds exactly;</item>
/// <item><c>bigint</c>, <c>oid</c> and <c>money</c>: strings, which it may not;</item>
/// <item>dates and times: ISO 8601. <c>timestamp with time zone</c> is UTC, with a <c>Z</c>, and the page
/// shows it in the viewer's zone (slice-02's rule); <c>infinity</c> as the word;</item>
/// <item><c>interval</c>: an ISO 8601 duration;</item>
/// <item>arrays and records: JSON arrays of their elements, read by the same rules;</item>
/// <item>any other type Npgsql can read: its text form (a <c>point</c> as <c>(1,2)</c>);</item>
/// <item>a type it cannot (an enum it has not loaded, <c>regclass</c>): the run is refused, naming the
/// column, and the user casts it to text.</item>
/// </list>
/// <para>A cell's bytes are the UTF-8 bytes of its value as returned: the string's, or the JSON
/// text of a number, boolean or array. A non-text cell whose wire form alone is over what is left is
/// counted over, unread, which may cut a row that would have fitted (an <c>integer[]</c> is about
/// four times smaller as JSON), but never reads one into memory that does not.</para>
/// </remarks>
static class ReportCells
{
    // pg_type.dat: built-in OIDs, fixed across PostgreSQL versions.
    const uint Bytea = 17, Name = 19, Text = 25, Json = 114, Xml = 142, Bpchar = 1042, Varchar = 1043, Interval = 1186, Numeric = 1700, Jsonb = 3802;

    static readonly HashSet<uint> TextTypes = [Name, Text, Json, Xml, Bpchar, Varchar, Jsonb];

    const int Chunk = 8192;

    public static bool CanRead(NpgsqlDataReader reader, int ordinal)
    {
        var oid = reader.GetDataTypeOID(ordinal);
        if (TextTypes.Contains(oid) || oid is Bytea or Numeric or Interval)
            return true;
        try
        {
            reader.GetFieldType(ordinal);
            return true;
        }
        catch (InvalidCastException)
        {
            return false; // no type information: Npgsql cannot read it at all
        }
    }

    /// <summary>The cell and its byte count, or null when it would exceed <paramref name="budget"/>.</summary>
    public static async Task<(JsonNode? Value, long Bytes)?> ReadAsync(NpgsqlDataReader reader, int ordinal, long budget, CancellationToken ct)
    {
        if (await reader.IsDBNullAsync(ordinal, ct))
            return (null, 0);

        var oid = reader.GetDataTypeOID(ordinal);
        if (TextTypes.Contains(oid))
            return await ReadTextAsync(reader, ordinal, budget, ct);
        if (oid == Bytea)
            return await ReadByteaAsync(reader, ordinal, budget, ct);

        // Not streamed: the wire form is checked first, so a wide array is never read whole.
        var wire = reader.GetBytes(ordinal, 0, null, 0, 0);
        if (wire > budget)
            return null;

        JsonNode? value;
        if (oid == Numeric)
        {
            var raw = new byte[wire];
            reader.GetBytes(ordinal, 0, raw, 0, raw.Length);
            value = JsonValue.Create(DecodeNumeric(raw));
        }
        else if (oid == Interval)
            value = JsonValue.Create(Iso(await reader.GetFieldValueAsync<NpgsqlInterval>(ordinal, ct)));
        else
            value = ToJson(await reader.GetFieldValueAsync<object>(ordinal, ct));

        // A void (pg_advisory_lock's) reads as null.
        var bytes = value switch
        {
            null => 0,
            JsonValue v when v.TryGetValue<string>(out var s) => Encoding.UTF8.GetByteCount(s),
            _ => Encoding.UTF8.GetByteCount(value.ToJsonString()),
        };
        return bytes > budget ? null : (value, bytes);
    }

    static async Task<(JsonNode?, long)?> ReadTextAsync(NpgsqlDataReader reader, int ordinal, long budget, CancellationToken ct)
    {
        using var text = await reader.GetTextReaderAsync(ordinal, ct);
        var encoder = Encoding.UTF8.GetEncoder(); // counts a surrogate pair split across chunks once
        var buffer = new char[Chunk];
        var result = new StringBuilder();
        long bytes = 0;
        int read;
        while ((read = await text.ReadAsync(buffer, ct)) > 0)
        {
            bytes += encoder.GetByteCount(buffer.AsSpan(0, read), flush: false);
            if (bytes > budget)
                return null;
            result.Append(buffer, 0, read);
        }
        return (JsonValue.Create(result.ToString()), bytes);
    }

    static async Task<(JsonNode?, long)?> ReadByteaAsync(NpgsqlDataReader reader, int ordinal, long budget, CancellationToken ct)
    {
        await using var stream = await reader.GetStreamAsync(ordinal, ct);
        var buffer = new byte[Chunk];
        var result = new StringBuilder("\\x");
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (result.Length + 2L * read > budget)
                return null;
            result.Append(Convert.ToHexStringLower(buffer, 0, read));
        }
        return result.Length > budget ? null : (JsonValue.Create(result.ToString()), result.Length);
    }

    /// <summary>
    /// PostgreSQL's binary <c>numeric</c>: digit count, weight (in base-10000 digits), sign, display
    /// scale, then the base-10000 digits. Written out as <c>numeric_out</c> writes it.
    /// </summary>
    public static string DecodeNumeric(byte[] raw)
    {
        var count = BinaryPrimitives.ReadInt16BigEndian(raw);
        var weight = BinaryPrimitives.ReadInt16BigEndian(raw.AsSpan(2));
        var sign = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(4));
        var scale = BinaryPrimitives.ReadInt16BigEndian(raw.AsSpan(6));
        switch (sign)
        {
            case 0xC000: return "NaN";
            case 0xD000: return "Infinity";
            case 0xF000: return "-Infinity";
        }
        int Digit(int index) => index >= 0 && index < count ? BinaryPrimitives.ReadInt16BigEndian(raw.AsSpan(8 + 2 * index)) : 0;

        var text = new StringBuilder();
        if (sign == 0x4000)
            text.Append('-');
        if (weight < 0)
            text.Append('0');
        else
            for (var d = 0; d <= weight; d++)
                text.Append(d == 0 ? Digit(d).ToString(CultureInfo.InvariantCulture) : Digit(d).ToString("D4", CultureInfo.InvariantCulture));
        if (scale > 0)
        {
            text.Append('.');
            for (var k = 0; k * 4 < scale; k++)
            {
                var group = Digit(weight + 1 + k).ToString("D4", CultureInfo.InvariantCulture);
                text.Append(group, 0, Math.Min(4, scale - k * 4));
            }
        }
        return text.ToString();
    }

    static JsonNode? ToJson(object? value) => value switch
    {
        null or DBNull => null,
        bool b => JsonValue.Create(b),
        short or int or byte or sbyte => JsonValue.Create(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
        float f => float.IsFinite(f) ? JsonValue.Create(f) : JsonValue.Create(NonFinite(f)),
        double d => double.IsFinite(d) ? JsonValue.Create(d) : JsonValue.Create(NonFinite(d)),
        long or uint or ulong or decimal => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
        string s => JsonValue.Create(s),
        char c => JsonValue.Create(c.ToString()),
        DateOnly d => JsonValue.Create(d == DateOnly.MaxValue ? "infinity" : d == DateOnly.MinValue ? "-infinity" : d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        TimeOnly t => JsonValue.Create(t.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.')),
        DateTime t => JsonValue.Create(
            t == DateTime.MaxValue ? "infinity" : t == DateTime.MinValue ? "-infinity"
            : t.ToString(t.Kind == DateTimeKind.Utc ? "yyyy-MM-ddTHH:mm:ss.FFFFFFFZ" : "yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).Replace(".Z", "Z").TrimEnd('.')),
        DateTimeOffset t => JsonValue.Create(t.ToString("HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture).Replace(".+", "+").Replace(".-", "-")),
        TimeSpan t => JsonValue.Create(Iso(new NpgsqlInterval(0, 0, t.Ticks / 10))),
        Guid g => JsonValue.Create(g.ToString("D")),
        IPAddress a => JsonValue.Create(a.ToString()),
        BitArray bits => JsonValue.Create(string.Concat(bits.Cast<bool>().Select(b => b ? '1' : '0'))),
        byte[] bytes => JsonValue.Create("\\x" + Convert.ToHexStringLower(bytes)),
        IEnumerable items => new JsonArray(items.Cast<object?>().Select(ToJson).ToArray()),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    static string NonFinite(double d) => double.IsNaN(d) ? "NaN" : d > 0 ? "Infinity" : "-Infinity";

    /// <summary>An ISO 8601 duration: <c>P1Y2M3DT4H5M6.5S</c>, <c>PT0S</c> for zero; each part carries its own sign.</summary>
    static string Iso(NpgsqlInterval interval)
    {
        var text = new StringBuilder("P");
        var (years, months) = (interval.Months / 12, interval.Months % 12);
        if (years != 0) text.Append(years).Append('Y');
        if (months != 0) text.Append(months).Append('M');
        if (interval.Days != 0) text.Append(interval.Days).Append('D');
        var time = TimeSpan.FromTicks(interval.Time * 10);
        if (time != TimeSpan.Zero)
        {
            text.Append('T');
            var hours = (long)time.TotalHours;
            if (hours != 0) text.Append(hours).Append('H');
            if (time.Minutes != 0) text.Append(time.Minutes).Append('M');
            var seconds = time.Seconds + time.Ticks % TimeSpan.TicksPerSecond / (decimal)TimeSpan.TicksPerSecond;
            if (seconds != 0) text.Append(seconds.ToString("0.######", CultureInfo.InvariantCulture)).Append('S');
        }
        return text.Length == 1 ? "PT0S" : text.ToString();
    }
}
