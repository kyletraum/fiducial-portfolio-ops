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
    Guid? Supersedes);

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
