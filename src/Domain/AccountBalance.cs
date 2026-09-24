namespace Portfolio.Domain;

/// <summary>
/// One observed balance, with its provenance (Constitution I and IV).
/// </summary>
/// <remarks>
/// M-9: a restatement never edits a row. The old row gets <see cref="SupersededAt"/>, and
/// the new row points BACK at it through <see cref="Supersedes"/>. Timestamps are UTC
/// (offset zero): Npgsql refuses to write a non-zero offset to timestamptz.
/// </remarks>
public class AccountBalance
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly AsOfDate { get; set; }
    public decimal Balance { get; set; }
    public string Currency { get; set; } = ReportingCurrency.Code;
    public required string SourceSystem { get; set; }
    public string? SourceId { get; set; }
    public SourceStrength SourceStrength { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public Guid? Supersedes { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Money Amount => new(Balance, Currency);
}
