using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Portfolio.Domain;

#nullable disable

namespace Portfolio.Infrastructure.Migrations
{
    /// <summary>
    /// Slice 03, migration 5: Identity's user store at schema Version3 (users, claims, logins,
    /// tokens, passkeys; no roles), the append-only audit_event, and used_setup_code, which makes
    /// a setup code single-use across an owner reset. Both new tables refuse UPDATE, DELETE and
    /// TRUNCATE at the database, as ingest_run does; that binds the application's code paths,
    /// not the connecting role (slice-03.md D6, gate G-7).
    /// </summary>
    public partial class AuthAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:audit_event_kind", "enrolment,sign_in_succeeded,sign_in_failed,sign_out,sessions_revoked,owner_reset")
                .Annotation("Npgsql:Enum:ingest_run_status", "succeeded,failed")
                .Annotation("Npgsql:Enum:source_strength", "statement,export,api,scrape,manual")
                .OldAnnotation("Npgsql:Enum:ingest_run_status", "succeeded,failed")
                .OldAnnotation("Npgsql:Enum:source_strength", "statement,export,api,scrape,manual");

            migrationBuilder.CreateTable(
                name: "audit_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    kind = table.Column<AuditEventKind>(type: "audit_event_kind", nullable: false),
                    actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    connection_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identity_user",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "used_setup_code",
                columns: table => new
                {
                    sha256 = table.Column<string>(type: "character(64)", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_used_setup_code", x => x.sha256);
                    table.CheckConstraint("used_setup_code_sha256_hex", "sha256 ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "identity_user_claim",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_user_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_user_claim_identity_user_user_id",
                        column: x => x.user_id,
                        principalTable: "identity_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_login",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_user_login", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_identity_user_login_identity_user_user_id",
                        column: x => x.user_id,
                        principalTable: "identity_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_passkey",
                columns: table => new
                {
                    credential_id = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_user_passkey", x => x.credential_id);
                    table.ForeignKey(
                        name: "fk_identity_user_passkey_identity_user_user_id",
                        column: x => x.user_id,
                        principalTable: "identity_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_token",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_user_token", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_identity_user_token_identity_user_user_id",
                        column: x => x.user_id,
                        principalTable: "identity_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "audit_event_newest_first",
                table: "audit_event",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "identity_user_email",
                table: "identity_user",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "identity_user_name_unique",
                table: "identity_user",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_user_claim_user_id",
                table: "identity_user_claim",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_identity_user_login_user_id",
                table: "identity_user_login",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_identity_user_passkey_user_id",
                table: "identity_user_passkey",
                column: "user_id");

            migrationBuilder.Sql("""
                CREATE FUNCTION append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION '% is append-only: % refused', TG_TABLE_NAME, TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER audit_event_append_only
                    BEFORE UPDATE OR DELETE ON audit_event
                    FOR EACH ROW EXECUTE FUNCTION append_only();
                CREATE TRIGGER audit_event_no_truncate
                    BEFORE TRUNCATE ON audit_event
                    FOR EACH STATEMENT EXECUTE FUNCTION append_only();

                CREATE TRIGGER used_setup_code_append_only
                    BEFORE UPDATE OR DELETE ON used_setup_code
                    FOR EACH ROW EXECUTE FUNCTION append_only();
                CREATE TRIGGER used_setup_code_no_truncate
                    BEFORE TRUNCATE ON used_setup_code
                    FOR EACH STATEMENT EXECUTE FUNCTION append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the tables drops their triggers; the function goes after.
            migrationBuilder.DropTable(
                name: "audit_event");

            migrationBuilder.DropTable(
                name: "identity_user_claim");

            migrationBuilder.DropTable(
                name: "identity_user_login");

            migrationBuilder.DropTable(
                name: "identity_user_passkey");

            migrationBuilder.DropTable(
                name: "identity_user_token");

            migrationBuilder.DropTable(
                name: "used_setup_code");

            migrationBuilder.DropTable(
                name: "identity_user");

            migrationBuilder.Sql("DROP FUNCTION append_only();");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:ingest_run_status", "succeeded,failed")
                .Annotation("Npgsql:Enum:source_strength", "statement,export,api,scrape,manual")
                .OldAnnotation("Npgsql:Enum:audit_event_kind", "enrolment,sign_in_succeeded,sign_in_failed,sign_out,sessions_revoked,owner_reset")
                .OldAnnotation("Npgsql:Enum:ingest_run_status", "succeeded,failed")
                .OldAnnotation("Npgsql:Enum:source_strength", "statement,export,api,scrape,manual");
        }
    }
}
