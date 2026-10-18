namespace Portfolio.Domain;

/// <summary>
/// FR-4.6: one import, recorded whatever its outcome - what ran, when, against what, what
/// changed, and whether it succeeded. IMMUTABLE once written: a trigger refuses UPDATE, DELETE
/// and TRUNCATE, so no code path, however wrong, can rewrite what an import did.
/// </summary>
/// <remarks>
/// All or nothing (slice-02.md step 2): a failed run wrote no balance, so its three counts are
/// zero; a succeeded run's counts add up to the rows it read. Both are CHECK constraints.
/// </remarks>
public class IngestRun
{
    public Guid Id { get; set; }
    public required string SourceSystem { get; set; }
    /// <summary>As the uploader named it. Data, never a path (FR-1.5).</summary>
    public required string FileName { get; set; }
    /// <summary>Lower-case hex SHA-256 of the bytes. The file itself is never stored.</summary>
    public required string FileSha256 { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>When the outcome was decided - the row is written once, at the end.</summary>
    public DateTimeOffset FinishedAt { get; set; }
    public IngestRunStatus Status { get; set; }
    public int RowsRead { get; set; }
    public int Created { get; set; }
    public int Restated { get; set; }
    public int Unchanged { get; set; }
    /// <summary>A JSON array of {location, reason} when the run failed; null when it succeeded.</summary>
    public string? ErrorDetail { get; set; }
}

/// <summary>No <c>partial</c>: this slice never imports part of a file (slice-02.md step 2).</summary>
public enum IngestRunStatus
{
    Succeeded,
    Failed,
}
