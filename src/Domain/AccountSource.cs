namespace Portfolio.Domain;

/// <summary>
/// M-10: where an account's external identity lives. (SourceSystem, SourceId) is unique
/// across ALL rows, not just live ones: a source id once seen is never re-bindable to a
/// different account.
/// </summary>
public class AccountSource
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string SourceSystem { get; set; }
    public required string SourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
