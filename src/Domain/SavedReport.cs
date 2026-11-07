namespace Portfolio.Domain;

/// <summary>
/// slice-04.md step 4 (FR-6.4): a named report, its SQL stored verbatim and run only on the reporting
/// login. Never edited (the middle cut, S4-7): delete and recreate.
/// </summary>
public class SavedReport
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>One statement, with its parameters written <c>$1 … $n</c> in declared order (SRC-4).</summary>
    public required string SqlText { get; set; }

    /// <summary>The declared parameters as JSON: an ordered array of <c>{"name", "type"}</c>.</summary>
    public required string Parameters { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
