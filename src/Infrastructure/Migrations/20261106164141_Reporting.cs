using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Migrations
{
    /// <summary>
    /// Slice 04, migration 6 (slice-04.md step 2): the <c>reporting</c> schema, the only surface user
    /// SQL reads (research.md D5, FR-6.2). Four views over the data that exists. Nothing in the EF
    /// model changes; every statement is SQL, asserted by tests/Integration (ReportingSchemaTests).
    /// </summary>
    /// <remarks>
    /// <para>Every view is <c>WITH (security_barrier)</c> (S4-5, DBS-4): without it a user's cheap
    /// function in a <c>WHERE</c> can be evaluated before the view's own filter, and see the rows it
    /// excludes. A catalog test fails on a view in <c>reporting</c> without it.</para>
    /// <para>Every view and column carries a <c>COMMENT</c>: the schema route reads them live from the
    /// catalog (FR-6.6), so this is the documentation, not a copy of it.</para>
    /// <para>The views read with their owner's rights (the Migrator's <c>postgres</c>), so the
    /// reporting login holds <c>SELECT</c> on them and nothing beneath them (DatabaseLogins).</para>
    /// </remarks>
    public partial class Reporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE SCHEMA reporting;
                COMMENT ON SCHEMA reporting IS
                    'The views a report may read, and nothing else. Soft-deleted and superseded rows are excluded, and no series is gap-filled. A column keeps its meaning; a change of meaning is a new column or a new view.';

                CREATE VIEW reporting.v_account WITH (security_barrier) AS
                SELECT a.id                         AS account_id,
                       a.display_name               AS account_name,
                       i.name                       AS institution_name,
                       a.currency,
                       a.opened_on,
                       a.closed_on,
                       a.closed_on IS NOT NULL      AS is_closed,
                       coalesce((SELECT array_agg(DISTINCT s.source_system ORDER BY s.source_system)
                                   FROM public.account_source s
                                  WHERE s.account_id = a.id), '{}') AS linked_formats,
                       a.created_at
                  FROM public.account a
                  JOIN public.institution i ON i.id = a.institution_id
                 WHERE a.deleted_at IS NULL;

                COMMENT ON VIEW reporting.v_account IS
                    'One row per account that has not been deleted, open or closed.';
                COMMENT ON COLUMN reporting.v_account.account_id IS 'The account''s identifier. v_balance_daily.account_id refers to it.';
                COMMENT ON COLUMN reporting.v_account.account_name IS 'The name the owner gave the account.';
                COMMENT ON COLUMN reporting.v_account.institution_name IS 'The institution that holds the account.';
                COMMENT ON COLUMN reporting.v_account.currency IS 'ISO 4217 code of the account''s balances.';
                COMMENT ON COLUMN reporting.v_account.opened_on IS 'The first day the account counts towards net worth.';
                COMMENT ON COLUMN reporting.v_account.closed_on IS 'The last day the account counts towards net worth; null while open. No balance is accepted after it.';
                COMMENT ON COLUMN reporting.v_account.is_closed IS 'True when a closing date is recorded.';
                COMMENT ON COLUMN reporting.v_account.linked_formats IS 'The import formats this account has an identifier linked under, such as import:csv; empty when none. The identifiers themselves are not shown.';
                COMMENT ON COLUMN reporting.v_account.created_at IS 'When the account was added (UTC).';

                CREATE VIEW reporting.v_balance_daily WITH (security_barrier) AS
                SELECT b.id                         AS balance_id,
                       b.account_id,
                       b.as_of_date,
                       b.balance,
                       b.currency,
                       b.source_system,
                       b.source_strength::text      AS source_strength,
                       b.observed_at,
                       b.source_as_of,
                       b.ingest_run_id
                  FROM public.account_balance b
                  JOIN public.account a ON a.id = b.account_id
                 WHERE b.deleted_at IS NULL
                   AND b.superseded_at IS NULL
                   AND a.deleted_at IS NULL;

                COMMENT ON VIEW reporting.v_balance_daily IS
                    'Each live balance: one row per account, day and source that has one. A restated balance shows only its newest value; days without a balance have no row (no gap-filling).';
                COMMENT ON COLUMN reporting.v_balance_daily.balance_id IS 'The balance''s identifier.';
                COMMENT ON COLUMN reporting.v_balance_daily.account_id IS 'The account it belongs to: v_account.account_id.';
                COMMENT ON COLUMN reporting.v_balance_daily.as_of_date IS 'The day the balance is about.';
                COMMENT ON COLUMN reporting.v_balance_daily.balance IS 'The balance, exact to four decimal places, as the source stated it.';
                COMMENT ON COLUMN reporting.v_balance_daily.currency IS 'ISO 4217 code of the balance.';
                COMMENT ON COLUMN reporting.v_balance_daily.source_system IS 'Where the balance came from: manual for one typed in, or the import format, such as import:csv.';
                COMMENT ON COLUMN reporting.v_balance_daily.source_strength IS 'How much the source can be trusted, strongest first: statement, export, api, scrape, manual.';
                COMMENT ON COLUMN reporting.v_balance_daily.observed_at IS 'When this system learned the balance (UTC).';
                COMMENT ON COLUMN reporting.v_balance_daily.source_as_of IS 'The instant the source stated the balance, when the source gives one (UTC); otherwise null.';
                COMMENT ON COLUMN reporting.v_balance_daily.ingest_run_id IS 'The import that wrote it: v_ingest_run.run_id. Null for a balance typed in.';

                CREATE VIEW reporting.v_net_worth_daily WITH (security_barrier) AS
                SELECT n.as_of_date,
                       n.net_worth,
                       n.accounts_in_window,
                       n.accounts_verified,
                       n.accounts_unverified,
                       n.accounts_carried,
                       n.max_staleness_days
                  FROM public.v_net_worth_daily n;

                COMMENT ON VIEW reporting.v_net_worth_daily IS
                    'Net worth by day, exactly as the page''s chart reads it: the same view, passed through, so a report and the chart agree.';
                COMMENT ON COLUMN reporting.v_net_worth_daily.as_of_date IS 'The day, from the first account''s opening to today.';
                COMMENT ON COLUMN reporting.v_net_worth_daily.net_worth IS 'The sum of the newest balance of each account open that day, within the staleness window; null when no account has one.';
                COMMENT ON COLUMN reporting.v_net_worth_daily.accounts_in_window IS 'Accounts open that day.';
                COMMENT ON COLUMN reporting.v_net_worth_daily.accounts_verified IS 'Accounts open that day with a balance within the staleness window.';
                COMMENT ON COLUMN reporting.v_net_worth_daily.accounts_unverified IS 'Accounts open that day with no balance within the window. They are not counted as zero: the total leaves them out.';
                COMMENT ON COLUMN reporting.v_net_worth_daily.accounts_carried IS 'Accounts whose balance is carried forward from an earlier day within the window.';
                COMMENT ON COLUMN reporting.v_net_worth_daily.max_staleness_days IS 'The age in days of the oldest balance counted that day; 0 when every balance is that day''s own.';

                CREATE VIEW reporting.v_ingest_run WITH (security_barrier) AS
                SELECT r.id                         AS run_id,
                       r.source_system,
                       r.file_name,
                       r.file_sha256,
                       r.started_at,
                       r.finished_at,
                       r.status::text               AS status,
                       r.rows_read,
                       r.created,
                       r.restated,
                       r.unchanged,
                       r.error_detail               AS problems
                  FROM public.ingest_run r;

                COMMENT ON VIEW reporting.v_ingest_run IS
                    'Every import attempted, succeeded or failed. An import is all or nothing: a failed one wrote no balance.';
                COMMENT ON COLUMN reporting.v_ingest_run.run_id IS 'The import''s identifier. v_balance_daily.ingest_run_id refers to it.';
                COMMENT ON COLUMN reporting.v_ingest_run.source_system IS 'The file''s format, such as import:csv or import:ofx.';
                COMMENT ON COLUMN reporting.v_ingest_run.file_name IS 'The file''s name as uploaded.';
                COMMENT ON COLUMN reporting.v_ingest_run.file_sha256 IS 'The SHA-256 of the file''s bytes, in hex.';
                COMMENT ON COLUMN reporting.v_ingest_run.started_at IS 'When the import started (UTC).';
                COMMENT ON COLUMN reporting.v_ingest_run.finished_at IS 'When it finished (UTC).';
                COMMENT ON COLUMN reporting.v_ingest_run.status IS 'succeeded or failed.';
                COMMENT ON COLUMN reporting.v_ingest_run.rows_read IS 'Balance rows read from the file.';
                COMMENT ON COLUMN reporting.v_ingest_run.created IS 'Balances new to this system. Zero when the import failed.';
                COMMENT ON COLUMN reporting.v_ingest_run.restated IS 'Balances that replaced an earlier value for the same account, day and source.';
                COMMENT ON COLUMN reporting.v_ingest_run.unchanged IS 'Balances already held with the same value.';
                COMMENT ON COLUMN reporting.v_ingest_run.problems IS 'A failed import''s problems, as a JSON array, each saying where in the file it was found; null when it succeeded.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Views only: no data lives here, so CASCADE drops nothing but the four views.
            migrationBuilder.Sql("DROP SCHEMA reporting CASCADE;");
        }
    }
}
