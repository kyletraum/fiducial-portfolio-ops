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
