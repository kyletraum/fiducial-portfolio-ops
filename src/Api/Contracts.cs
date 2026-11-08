using System.ComponentModel;
using Portfolio.Domain;

namespace Portfolio.Api;

// S-27: explicit request and response DTOs in BOTH directions. No entity is ever bound from a
// request - a bound AccountBalance would let a caller set source_strength and forge provenance.
//
// Money is a decimal STRING, both ways. numeric(19,4) holds up to 19 significant digits; a
// JavaScript number holds about 15, so a JSON number would round a figure in transit, and
// Constitution I forbids a figure the system did not observe.

public sealed record CreateAccountRequest(
    [property: Description("Institution name. Created if no institution has it yet.")]
    string InstitutionName,
    string DisplayName,
    DateOnly OpenedOn);

public sealed record AccountResponse(
    Guid Id,
    string InstitutionName,
    string DisplayName,
    string Currency,
    DateOnly OpenedOn,
    DateOnly? ClosedOn,
    [property: Description("The newest live balance, as a decimal string; null if none recorded.")]
    string? LatestBalance,
    DateOnly? LatestAsOfDate);

public sealed record RecordBalanceRequest(
    DateOnly AsOfDate,
    [property: Description("Decimal string, at most 4 decimal places, e.g. \"1234.5678\".")]
    string Balance);

public sealed record BalanceResponse(
    Guid Id,
    Guid AccountId,
    DateOnly AsOfDate,
    [property: Description("Decimal string at scale 4.")]
    string Balance,
    string Currency,
    string SourceSystem,
    SourceStrength SourceStrength,
    DateTimeOffset ObservedAt,
    [property: Description("The balance this one restated, if any.")]
    Guid? Supersedes,
    [property: Description("The import run that wrote this balance; null for one entered by hand.")]
    Guid? IngestRunId,
    [property: Description("The source's own stamp as a UTC moment, when it carried a time. Provenance only: asOfDate is the date as written. Display it in the viewer's time zone.")]
    DateTimeOffset? SourceAsOf);

public enum NetWorthStatus
{
    /// <summary>Every in-window account has a value in the staleness window.</summary>
    Verified,
    /// <summary>Some do; <c>netWorth</c> sums only those.</summary>
    Partial,
    /// <summary>None do. <c>netWorth</c> is null - UNVERIFIED, never zero (Constitution II).</summary>
    Unverified,
}

public sealed record NetWorthPoint(
    DateOnly AsOfDate,
    [property: Description("Decimal string; null when no account is verified - never read it as zero.")]
    string? NetWorth,
    NetWorthStatus Status,
    long AccountsInWindow,
    long AccountsVerified,
    long AccountsUnverified,
    long AccountsCarried,
    int? MaxStalenessDays);

public sealed record NetWorthResponse(
    DateOnly From,
    DateOnly To,
    [property: Description("Days after which a carried balance goes unverified (D-034).")]
    int StalenessDays,
    IReadOnlyList<NetWorthPoint> Points);

public sealed record HealthResponse(string Status);

// ---- Slice 02: bindings and imports ----

public sealed record BindSourceRequest(
    [property: Description("The source system the id belongs to: \"import:csv\" or \"import:ofx\".")]
    string SourceSystem,
    [property: Description("The file's own identifier for this account: the CSV 'account' column or the OFX ACCTID.")]
    string SourceId);

public sealed record SourceResponse(Guid AccountId, string SourceSystem, string SourceId, DateTimeOffset CreatedAt);

public sealed record ImportErrorResponse(
    [property: Description("Where in the file: 'line 3', 'statement 2', or 'file'.")]
    string Location,
    [property: Description("Why. File text in it is quoted, bounded and escaped.")]
    string Reason);

public enum ImportStatus
{
    /// <summary>Every row was imported: created, restated or unchanged.</summary>
    Succeeded,
    /// <summary>Nothing was imported. All or nothing: one refused row fails the file.</summary>
    Failed,
}

public sealed record ImportRunResponse(
    Guid Id,
    string SourceSystem,
    [property: Description("As the uploader named it. Data, never a path.")]
    string FileName,
    [property: Description("Lower-case hex SHA-256 of the file. The file itself is not kept.")]
    string FileSha256,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    ImportStatus Status,
    int RowsRead,
    int Created,
    int Restated,
    int Unchanged,
    [property: Description("Why the run failed; empty when it succeeded. At most 50, then a count of the rest.")]
    IReadOnlyList<ImportErrorResponse> Errors);

// ---- Slice 03: authentication. No response carries a credential (FR-3.2): the passkey options are
// Identity's own WebAuthn JSON, which holds a challenge and public identifiers only.

public sealed record AuthStateResponse(
    [property: Description("Whether an owner has enrolled. Before that, the page offers set-up instead of sign-in.")]
    bool Enrolled,
    [property: Description("The environment's name, shown on the sign-in page before authentication.")]
    string Environment);

public sealed record BeginEnrolmentRequest(
    [property: Description("The first-run setup code. Spaces, hyphens and case are ignored.")]
    string SetupCode,
    string UserName);

public sealed record CompleteEnrolmentRequest(
    [property: Description("The same setup code, presented again with the passkey.")]
    string SetupCode,
    [property: Description("The browser's PublicKeyCredential.toJSON() from navigator.credentials.create().")]
    System.Text.Json.JsonElement Credential);

public sealed record SessionResponse(string UserName);

public sealed record CsrfTokenResponse(
    [property: Description("Send it as the X-CSRF-TOKEN header on every unsafe request. Bound to the signed-in user: fetch a new one after every sign-in.")]
    string Token);

/// <summary>
/// The ProblemDetails titles the authentication routes answer with. Part of the contract: the page
/// tells one refusal from another by its title, never its status (slice-03.md step 4).
/// </summary>
public static class ProblemTitles
{
    public const string SetupCodeRefused = "Setup code not accepted";
    public const string AlreadyEnrolled = "An owner is already enrolled";
    public const string NoCeremony = "No passkey ceremony is underway";
    public const string PasskeyRefused = "Passkey not accepted";
    public const string NotRegisteredHere = "This passkey isn't registered with this app";
    public const string CsrfTokenRefused = "CSRF token missing or invalid";
    public const string TooManySignInAttempts = "Too many sign-in attempts. Try again in about a minute.";
}

// Slice 04 step 3: reports. The SQL is the user's own, run on the reporting login (slice-04.md).

public sealed record RunReportRequest(
    [property: Description("One SQL statement, over the views of the reporting schema. Run read-only, then rolled back.")]
    string Sql);

public sealed record ReportColumnResponse(
    string Name,
    [property: Description("The PostgreSQL type, e.g. numeric or timestamp with time zone.")]
    string Type);

public sealed record ReportRunResponse(
    IReadOnlyList<ReportColumnResponse> Columns,
    [property: Description("One array per row, in column order. numeric, bigint and money are strings, so every digit is kept; timestamp with time zone is UTC, ISO 8601.")]
    IReadOnlyList<IReadOnlyList<System.Text.Json.Nodes.JsonNode?>> Rows,
    [property: Description("True when the result is not all there: see cutOffBy.")]
    bool CutOff,
    [property: Description("What cut it off: the row cap, the byte cap, or the deadline. Null when nothing did.")]
    Portfolio.Infrastructure.ReportCutOff? CutOffBy);

public sealed record ReportSchemaResponse(IReadOnlyList<ReportViewResponse> Views);

public sealed record ReportViewResponse(
    [property: Description("The view, in the reporting schema: reporting.<name>.")]
    string Name,
    [property: Description("The view's COMMENT, read live from the database.")]
    string? Description,
    IReadOnlyList<ReportViewColumnResponse> Columns);

public sealed record ReportViewColumnResponse(
    string Name,
    string Type,
    [property: Description("The column's COMMENT, read live from the database.")]
    string? Description);

// Slice 04 step 4: saved reports. Created, listed, run and deleted; never edited (S4-7).

public sealed record SavedReportParameter(
    [property: Description("Lower case letters, digits and underscores, starting with a letter. The page labels its input with it.")]
    string Name,
    Portfolio.Infrastructure.ReportParameterType Type);

public sealed record CreateSavedReportRequest(
    string Name,
    string? Description,
    [property: Description("One SQL statement. The parameters are written $1, $2, … in the order declared, and bound, never written into the text.")]
    string Sql,
    IReadOnlyList<SavedReportParameter>? Parameters);

public sealed record SavedReportResponse(
    Guid Id,
    string Name,
    string? Description,
    string Sql,
    IReadOnlyList<SavedReportParameter> Parameters,
    DateTimeOffset CreatedAt);

public sealed record RunSavedReportRequest(
    [property: Description("A value per declared parameter, by name, as a string: a date as yyyy-MM-dd, a number with an optional sign and decimal point. Null binds SQL NULL.")]
    IReadOnlyDictionary<string, string?>? Parameters);
