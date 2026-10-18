using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Portfolio.Domain;

#nullable disable

namespace Portfolio.Infrastructure.Migrations
{
    /// <summary>
    /// Slice 02, migration 4 - applied to a database that already holds rows, as migration 2 was:
    /// ingest_run (immutable), account_balance.ingest_run_id and source_as_of, the database time
    /// zone pinned to UTC, and the net-worth view's same-day tie-break (Spike G).
    /// </summary>
    public partial class IngestRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:ingest_run_status", "succeeded,failed")
                .Annotation("Npgsql:Enum:source_strength", "statement,export,api,scrape,manual")
                .OldAnnotation("Npgsql:Enum:source_strength", "statement,export,api,scrape,manual");

            migrationBuilder.AddColumn<Guid>(
                name: "ingest_run_id",
                table: "account_balance",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "source_as_of",
                table: "account_balance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ingest_run",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_system = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_sha256 = table.Column<string>(type: "character(64)", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<IngestRunStatus>(type: "ingest_run_status", nullable: false),
                    rows_read = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<int>(type: "integer", nullable: false),
                    restated = table.Column<int>(type: "integer", nullable: false),
                    unchanged = table.Column<int>(type: "integer", nullable: false),
                    error_detail = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingest_run", x => x.id);
                    table.CheckConstraint("ingest_run_all_or_nothing", "(status = 'failed' AND created = 0 AND restated = 0 AND unchanged = 0 AND error_detail IS NOT NULL) OR (status = 'succeeded' AND created + restated + unchanged = rows_read AND error_detail IS NULL)");
                    table.CheckConstraint("ingest_run_counts_nonnegative", "rows_read >= 0 AND created >= 0 AND restated >= 0 AND unchanged >= 0");
                    table.CheckConstraint("ingest_run_finished_after_started", "finished_at >= started_at");
                    table.CheckConstraint("ingest_run_sha256_hex", "file_sha256 ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_balance_ingest_run_id",
                table: "account_balance",
                column: "ingest_run_id");

            migrationBuilder.CreateIndex(
                name: "ingest_run_newest_first",
                table: "ingest_run",
                column: "started_at",
                descending: new bool[0]);

            migrationBuilder.AddForeignKey(
                name: "fk_account_balance_ingest_runs_ingest_run_id",
                table: "account_balance",
                column: "ingest_run_id",
                principalTable: "ingest_run",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // --- Not expressible in the EF model. Each block below is asserted by
            // --- tests/Integration (ImportPipelineTests, SameDayPickTests).

            // FR-4.6: a run is immutable once written - refused at the database, so no code path
            // can rewrite what an import did. TRUNCATE too: it fires no row trigger. DROP TABLE
            // (this migration's Down) is not refused.
            migrationBuilder.Sql("""
                CREATE FUNCTION ingest_run_immutable() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'ingest_run is immutable (FR-4.6): % refused', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER ingest_run_immutable
                    BEFORE UPDATE OR DELETE ON ingest_run
                    FOR EACH ROW EXECUTE FUNCTION ingest_run_immutable();

                CREATE TRIGGER ingest_run_no_truncate
                    BEFORE TRUNCATE ON ingest_run
                    FOR EACH STATEMENT EXECUTE FUNCTION ingest_run_immutable();
                """);

            // slice-02.md "Dates and time": the data layer is UTC by DECLARATION, not because the
            // postgres image happens to default to it - current_date (the spine's "today") reads
            // the session time zone. Applies to new sessions; the database name differs per
            // environment and in tests, so it is read, not written here.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    EXECUTE format('ALTER DATABASE %I SET timezone TO %L', current_database(), 'UTC');
                END $$;
                """);

            // Spike G, ruled by Kyle 2026-10-18: the same day from two sources was decided by
            // WRITE ORDER. Newest date, then the strongest source, then the newest observation;
            // id last only so the order is total. Only the lateral's ORDER BY changes.
            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW v_net_worth_daily AS
                SELECT
                    s.as_of_date,
                    sum(o.balance)                                       AS net_worth,
                    count(*)                                             AS accounts_in_window,
                    count(o.balance)                                     AS accounts_verified,
                    count(*) - count(o.balance)                          AS accounts_unverified,
                    count(*) FILTER (WHERE o.as_of_date < s.as_of_date)  AS accounts_carried,
                    max(s.as_of_date - o.as_of_date)                     AS max_staleness_days
                FROM v_date_spine s
                JOIN account a
                  ON a.deleted_at IS NULL
                 AND s.as_of_date >= a.opened_on
                 AND s.as_of_date <= coalesce(a.closed_on, 'infinity'::date)
                LEFT JOIN LATERAL (
                    SELECT b.balance, b.as_of_date
                      FROM account_balance b
                     WHERE b.account_id = a.id
                       AND b.deleted_at IS NULL
                       AND b.superseded_at IS NULL
                       AND b.as_of_date <= s.as_of_date
                       AND b.as_of_date >= s.as_of_date
                                           - (SELECT balance_staleness_days FROM app_setting)
                     ORDER BY b.as_of_date DESC, b.source_strength ASC, b.observed_at DESC, b.id DESC
                     LIMIT 1
                ) o ON true
                GROUP BY s.as_of_date;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The pre-slice-02 view, exactly as migration Initial wrote it.
            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW v_net_worth_daily AS
                SELECT
                    s.as_of_date,
                    sum(o.balance)                                       AS net_worth,
                    count(*)                                             AS accounts_in_window,
                    count(o.balance)                                     AS accounts_verified,
                    count(*) - count(o.balance)                          AS accounts_unverified,
                    count(*) FILTER (WHERE o.as_of_date < s.as_of_date)  AS accounts_carried,
                    max(s.as_of_date - o.as_of_date)                     AS max_staleness_days
                FROM v_date_spine s
                JOIN account a
                  ON a.deleted_at IS NULL
                 AND s.as_of_date >= a.opened_on
                 AND s.as_of_date <= coalesce(a.closed_on, 'infinity'::date)
                LEFT JOIN LATERAL (
                    SELECT b.balance, b.as_of_date
                      FROM account_balance b
                     WHERE b.account_id = a.id
                       AND b.deleted_at IS NULL
                       AND b.superseded_at IS NULL
                       AND b.as_of_date <= s.as_of_date
                       AND b.as_of_date >= s.as_of_date
                                           - (SELECT balance_staleness_days FROM app_setting)
                     ORDER BY b.as_of_date DESC
                     LIMIT 1
                ) o ON true
                GROUP BY s.as_of_date;
                """);

            migrationBuilder.Sql("""
                DO $$ BEGIN
                    EXECUTE format('ALTER DATABASE %I RESET timezone', current_database());
                END $$;
                """);

            migrationBuilder.Sql("""
                DROP TRIGGER ingest_run_no_truncate ON ingest_run;
                DROP TRIGGER ingest_run_immutable ON ingest_run;
                DROP FUNCTION ingest_run_immutable();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_account_balance_ingest_runs_ingest_run_id",
                table: "account_balance");

            migrationBuilder.DropTable(
                name: "ingest_run");

            migrationBuilder.DropIndex(
                name: "ix_account_balance_ingest_run_id",
                table: "account_balance");

            migrationBuilder.DropColumn(
                name: "ingest_run_id",
                table: "account_balance");

            migrationBuilder.DropColumn(
                name: "source_as_of",
                table: "account_balance");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:source_strength", "statement,export,api,scrape,manual")
                .OldAnnotation("Npgsql:Enum:ingest_run_status", "succeeded,failed")
                .OldAnnotation("Npgsql:Enum:source_strength", "statement,export,api,scrape,manual");
        }
    }
}
