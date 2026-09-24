namespace Portfolio.Domain;

/// <summary>
/// M-10: an INTERNAL entity. It carries no external identity - that lives on
/// <see cref="AccountSource"/> - so nothing here changes when a second source starts
/// reporting the same real-world account.
/// </summary>
public class Account
{
    public Guid Id { get; set; }
    public Guid InstitutionId { get; set; }
    public required string DisplayName { get; set; }
    public string Currency { get; set; } = ReportingCurrency.Code;
    public DateOnly OpenedOn { get; set; }
    /// <summary>Constitution III: past this date no balance exists and none is carried.</summary>
    public DateOnly? ClosedOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
