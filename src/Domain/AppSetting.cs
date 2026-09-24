namespace Portfolio.Domain;

/// <summary>
/// Single-row settings. Amendment 3 condition 2: the staleness cutoff is a stored
/// setting, never a literal in a view body.
/// </summary>
public class AppSetting
{
    /// <summary>Always true; a CHECK keeps the table to one row.</summary>
    public bool Id { get; set; } = true;

    /// <summary>D-034, ruled 2026-09-20: 90 days.</summary>
    public int BalanceStalenessDays { get; set; }

    public const int DefaultBalanceStalenessDays = 90;
}
