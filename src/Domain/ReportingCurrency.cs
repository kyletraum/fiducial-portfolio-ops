namespace Portfolio.Domain;

/// <summary>
/// M-11: slice 01 is single-currency. Every account and balance is in this currency,
/// enforced by a CHECK on each table rather than a cross-row constraint (R2-M9).
/// </summary>
public static class ReportingCurrency
{
    public const string Code = "USD";
}
