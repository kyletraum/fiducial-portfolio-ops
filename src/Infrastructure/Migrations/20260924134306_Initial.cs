using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Portfolio.Domain;

#nullable disable

namespace Portfolio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:source_strength", "statement,export,api,scrape,manual");

            migrationBuilder.CreateTable(
                name: "app_setting",
                columns: table => new
                {
                    id = table.Column<bool>(type: "boolean", nullable: false),
                    balance_staleness_days = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_setting", x => x.id);
                    table.CheckConstraint("app_setting_single_row", "id");
                    table.CheckConstraint("app_setting_staleness_positive", "balance_staleness_days > 0");
                });

            migrationBuilder.CreateTable(
                name: "institution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_institution", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    institution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "character(3)", nullable: false),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account", x => x.id);
                    table.CheckConstraint("account_single_currency", "currency = 'USD'");
                    table.CheckConstraint("account_window_ordered", "closed_on IS NULL OR closed_on >= opened_on");
                    table.ForeignKey(
                        name: "fk_account_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institution",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_balance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    currency = table.Column<string>(type: "character(3)", nullable: false),
                    source_system = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: true),
                    source_strength = table.Column<SourceStrength>(type: "source_strength", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    supersedes = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_balance", x => x.id);
                    table.CheckConstraint("account_balance_single_currency", "currency = 'USD'");
                    table.ForeignKey(
                        name: "fk_account_balance_account_balance_supersedes",
                        column: x => x.supersedes,
                        principalTable: "account_balance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_balance_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "app_setting",
                columns: new[] { "id", "balance_staleness_days" },
                values: new object[] { true, 90 });

            migrationBuilder.CreateIndex(
                name: "ix_account_institution_id",
                table: "account",
                column: "institution_id");

            migrationBuilder.CreateIndex(
                name: "account_balance_live_uq",
                table: "account_balance",
                columns: new[] { "account_id", "as_of_date", "source_system" },
                unique: true,
                filter: "deleted_at IS NULL AND superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "account_balance_series",
                table: "account_balance",
                columns: new[] { "account_id", "as_of_date" },
                descending: new[] { false, true },
                filter: "deleted_at IS NULL AND superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "account_balance_supersedes_uq",
                table: "account_balance",
                column: "supersedes",
                unique: true,
                filter: "supersedes IS NOT NULL");

            // --- Not expressible in the EF model. Copied from spikes/spike-d-sql (01, 03), where
            // --- every line below was asserted against PostgreSQL. Inline, not shared constants:
            // --- a migration is a frozen record and must not change when later code does.

            // S-17: the closed_on guard cannot be a CHECK (it reads another table). A trigger, in
            // BOTH directions - the one that actually happens is closing an account after its
            // balances exist.
            migrationBuilder.Sql("""
                CREATE FUNCTION account_balance_window_guard() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    a_opened date;
                    a_closed date;
                BEGIN
                    SELECT opened_on, closed_on INTO a_opened, a_closed
                      FROM account WHERE id = NEW.account_id;

                    IF NEW.as_of_date < a_opened THEN
                        RAISE EXCEPTION 'balance % precedes account opened_on %', NEW.as_of_date, a_opened
                            USING ERRCODE = 'check_violation';
                    END IF;

                    IF a_closed IS NOT NULL AND NEW.as_of_date > a_closed THEN
                        RAISE EXCEPTION 'balance % follows account closed_on % (Constitution III)',
                            NEW.as_of_date, a_closed
                            USING ERRCODE = 'check_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER account_balance_window_guard
                    BEFORE INSERT OR UPDATE OF as_of_date, account_id ON account_balance
                    FOR EACH ROW EXECUTE FUNCTION account_balance_window_guard();

                CREATE FUNCTION account_close_guard() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    latest date;
                BEGIN
                    IF NEW.closed_on IS NULL THEN
                        RETURN NEW;
                    END IF;

                    SELECT max(as_of_date) INTO latest
                      FROM account_balance
                     WHERE account_id = NEW.id
                       AND deleted_at IS NULL
                       AND superseded_at IS NULL;

                    IF latest IS NOT NULL AND latest > NEW.closed_on THEN
                        RAISE EXCEPTION 'account has a live balance at % after the proposed closed_on %',
                            latest, NEW.closed_on
                            USING ERRCODE = 'check_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER account_close_guard
                    BEFORE UPDATE OF closed_on ON account
                    FOR EACH ROW EXECUTE FUNCTION account_close_guard();
                """);

            // M-1 / Amendment 3: carry-forward within each account's active window, the carry
            // expiring after app_setting.balance_staleness_days (D-034: 90). Three rules, none
            // stylistic: LEFT JOIN LATERAL ... ON true, never CROSS JOIN (which makes the
            // unverified count identically zero); ORDER BY ... LIMIT 1, not DISTINCT ON; and
            // sum() over all-NULLs stays NULL - a day with nothing verified is UNVERIFIED
            // (Constitution II), never zero.
            migrationBuilder.Sql("""
                CREATE VIEW v_date_spine AS
                SELECT d::date AS as_of_date
                  FROM generate_series(
                        (SELECT min(opened_on) FROM account WHERE deleted_at IS NULL),
                        greatest(
                          (SELECT max(as_of_date) FROM account_balance
                            WHERE deleted_at IS NULL AND superseded_at IS NULL)
                            + (SELECT balance_staleness_days FROM app_setting),
                          (SELECT max(closed_on) FROM account WHERE deleted_at IS NULL)),
                        interval '1 day') AS d;

                CREATE VIEW v_net_worth_daily AS
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP VIEW v_net_worth_daily;
                DROP VIEW v_date_spine;
                DROP TRIGGER account_close_guard ON account;
                DROP FUNCTION account_close_guard();
                DROP TRIGGER account_balance_window_guard ON account_balance;
                DROP FUNCTION account_balance_window_guard();
                """);

            migrationBuilder.DropTable(
                name: "account_balance");

            migrationBuilder.DropTable(
                name: "app_setting");

            migrationBuilder.DropTable(
                name: "account");

            migrationBuilder.DropTable(
                name: "institution");
        }
    }
}
