namespace Portfolio.Infrastructure;

/// <summary>
/// slice-04.md "Values" (S4-2): the bounds on a report run, from configuration (section
/// <c>Reporting</c>), so tests set them small (SCO-12). The defaults are Production's, and
/// <c>testing.md</c> names them.
/// </summary>
/// <remarks>
/// The Api reads the first three, which it enforces on the client. The Migrator reads the deadline
/// and <see cref="TempFileLimitMegabytes"/>, which <see cref="DatabaseLogins"/> sets as the
/// reporting login's role defaults (DBS-2): a backstop, except <c>temp_file_limit</c>, which only a
/// superuser can change and so is the one that holds.
/// </remarks>
public sealed class ReportingLimits
{
    public const string Section = "Reporting";

    /// <summary>One wall-clock deadline for the whole run, connecting included (part 4).</summary>
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Rows returned at most; the row that would cross it is dropped and the run cut off.</summary>
    public int MaxRows { get; set; } = 10_000;

    /// <summary>
    /// UTF-8 bytes of returned cells at most (R2-TST-6): a cell is counted by its value as returned.
    /// The row whose cell crosses it is dropped and the run cut off.
    /// </summary>
    public int MaxBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>The reporting login's <c>temp_file_limit</c>, a role default only a superuser can lift.</summary>
    public int TempFileLimitMegabytes { get; set; } = 256;

    /// <summary>
    /// The server's own <c>statement_timeout</c> for a run, set by the runner: a second past the
    /// client's deadline, so the client's cancel is what ends a run, and this ends one whose client
    /// has stopped listening. The statement can lift it (R2-TST-7's first producer); the client's
    /// deadline it cannot reach.
    /// </summary>
    public TimeSpan StatementTimeout => Deadline + TimeSpan.FromSeconds(1);

    /// <summary>
    /// The role defaults' timeouts (<c>statement_timeout</c>, <c>transaction_timeout</c>,
    /// <c>idle_in_transaction_session_timeout</c>): twice the deadline. They bound a session the Api
    /// lost track of, and a connection opened on the login by anything but the runner.
    /// </summary>
    public TimeSpan RoleTimeout => Deadline * 2;
}
