namespace Portfolio.Ingestion;

/// <summary>
/// A connector's whole job (plan.md "Ingestion"): bytes in, parsed rows out. It never reaches
/// the database; the import pipeline decides what the rows mean to the data.
/// </summary>
public interface IBalanceSource
{
    /// <summary>The <c>source_system</c> its rows are recorded under, e.g. <c>import:csv</c>.</summary>
    string SourceSystem { get; }

    ParseResult Parse(ReadOnlySpan<byte> content);
}

/// <param name="Location">Where in the file: <c>line 3</c>, <c>statement 2</c>.</param>
/// <param name="SourceId">The file's own identifier for the account. Matched against
/// <c>account_source</c> by the pipeline; never used to create or guess an account.</param>
/// <param name="AsOfDate">A calendar date, as the file wrote it - never converted
/// (slice-02.md "Dates and time").</param>
/// <param name="SourceAsOf">The source's own stamp as a UTC moment, when it carried a time;
/// null otherwise. Provenance only - nothing is dated by it.</param>
public sealed record ParsedBalance(string Location, string SourceId, DateOnly AsOfDate, decimal Amount, DateTimeOffset? SourceAsOf);

public sealed record ParseError(string Location, string Reason);

/// <summary>
/// ALL OR NOTHING (slice-02.md step 2): a result with any error carries no balances, so a
/// caller cannot import the good half of a bad file by mistake. Every error is still reported,
/// up to <see cref="IngestionLimits.MaxErrorsReported"/>.
/// </summary>
public sealed record ParseResult
{
    public IReadOnlyList<ParsedBalance> Balances { get; }
    public IReadOnlyList<ParseError> Errors { get; }
    public bool Succeeded => Errors.Count == 0;

    ParseResult(IReadOnlyList<ParsedBalance> balances, IReadOnlyList<ParseError> errors) =>
        (Balances, Errors) = (balances, errors);

    internal static ParseResult From(List<ParsedBalance> balances, List<ParseError> errors) =>
        errors.Count == 0 ? new(balances, []) : new([], Bounded(errors));

    internal static ParseResult Refused(string location, string reason) => new([], [new(location, reason)]);

    static List<ParseError> Bounded(List<ParseError> errors)
    {
        if (errors.Count <= IngestionLimits.MaxErrorsReported) return errors;
        var kept = errors.Take(IngestionLimits.MaxErrorsReported).ToList();
        kept.Add(new("file", $"and {errors.Count - IngestionLimits.MaxErrorsReported} more errors, not listed"));
        return kept;
    }
}

public static class IngestionLimits
{
    /// <summary>slice-02.md Spike F: sized for a balance file, not for transactions.</summary>
    public const int MaxRows = 10_000;

    /// <summary>OFX's ACCTID is at most 22 characters; this leaves room for a CSV's own ids.</summary>
    public const int MaxSourceIdLength = 64;

    public const int MaxErrorsReported = 50;

    /// <summary>FR-1.5: an error quotes the file's text, but never more than this much of it.</summary>
    public const int MaxQuotedLength = 80;
}

static class Text
{
    /// <summary>
    /// FR-1.5: file text in an error message is data. Bounded, and control characters shown as
    /// escapes, so a refused line cannot inject a line break or terminal sequence into a log or
    /// a page.
    /// </summary>
    public static string Quote(string? value)
    {
        if (value is null) return "(none)";
        var cut = value.Length > IngestionLimits.MaxQuotedLength ? value[..IngestionLimits.MaxQuotedLength] + "..." : value;
        var sb = new System.Text.StringBuilder(cut.Length + 2).Append('\'');
        foreach (var c in cut)
            sb.Append(char.IsControl(c) ? $"\\u{(int)c:x4}" : c);
        return sb.Append('\'').ToString();
    }

    /// <summary>A source id is non-empty, bounded, has no control characters and no edge whitespace.</summary>
    public static string? SourceIdProblem(string id)
    {
        if (id.Length == 0) return "is empty";
        if (id.Length > IngestionLimits.MaxSourceIdLength) return $"is longer than {IngestionLimits.MaxSourceIdLength} characters";
        if (id.Any(char.IsControl)) return "contains a control character";
        if (char.IsWhiteSpace(id[0]) || char.IsWhiteSpace(id[^1])) return "starts or ends with whitespace (refused, not trimmed: it is an identity)";
        return null;
    }
}
