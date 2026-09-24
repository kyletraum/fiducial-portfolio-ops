namespace Portfolio.Infrastructure.Configurations;

static class Conventions
{
    public const string NewUuid = "gen_random_uuid()";
    public const string Now = "now()";
    public const string Currency = "character(3)";
    public const string Money = "numeric(19,4)";

    /// <summary>M-11: a literal CHECK per table, not a cross-row constraint (R2-M9).</summary>
    public const string SingleCurrency = "currency = '" + Domain.ReportingCurrency.Code + "'";

    /// <summary>A balance row that counts: neither soft-deleted nor restated.</summary>
    public const string LiveBalance = "deleted_at IS NULL AND superseded_at IS NULL";
}
