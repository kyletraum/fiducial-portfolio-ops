using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpineRunsToToday : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Found by the step-5 integration test: the spine ended at the later of "last
            // balance + cutoff" and "latest closed_on". Past that, days on which accounts were
            // OPEN and unverified had no row at all - so the series stopped instead of saying
            // UNVERIFIED, which Constitution II forbids (the spike's own comment says the same
            // of the spine's START). The endpoint's WHERE cannot add rows the view never made.
            // Now the spine also runs to today.
            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW v_date_spine AS
                SELECT d::date AS as_of_date
                  FROM generate_series(
                        (SELECT min(opened_on) FROM account WHERE deleted_at IS NULL),
                        greatest(
                          (SELECT max(as_of_date) FROM account_balance
                            WHERE deleted_at IS NULL AND superseded_at IS NULL)
                            + (SELECT balance_staleness_days FROM app_setting),
                          (SELECT max(closed_on) FROM account WHERE deleted_at IS NULL),
                          current_date),
                        interval '1 day') AS d;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The pre-fix definition, exactly as migration Initial wrote it.
            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW v_date_spine AS
                SELECT d::date AS as_of_date
                  FROM generate_series(
                        (SELECT min(opened_on) FROM account WHERE deleted_at IS NULL),
                        greatest(
                          (SELECT max(as_of_date) FROM account_balance
                            WHERE deleted_at IS NULL AND superseded_at IS NULL)
                            + (SELECT balance_staleness_days FROM app_setting),
                          (SELECT max(closed_on) FROM account WHERE deleted_at IS NULL)),
                        interval '1 day') AS d;
                """);
        }
    }
}
