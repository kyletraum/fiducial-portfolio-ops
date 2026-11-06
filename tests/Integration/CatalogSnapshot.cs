using Npgsql;

namespace Portfolio.Integration;

/// <summary>
/// slice-04.md step 1a: everything a login can do, read from the catalog as EFFECTIVE privileges
/// (has_*_privilege), so a grant through PUBLIC or a role membership counts as a grant (R2-DBS-3).
/// One line per fact, sorted, compared with a committed file (TST-4, R2-TST-4). Databases are named
/// by role, so one expected file serves Integration and the published stack.
/// </summary>
static class CatalogSnapshot
{
    /// <summary>
    /// Named built-ins whose EXECUTE matters: the ones slice 04 revokes, and the ones a reader of
    /// the snapshot would ask about. pg_catalog as a whole is PUBLIC's by design (SCO-13).
    /// </summary>
    static readonly string[] BuiltIns =
    [
        "lo_create", "lo_creat", "lo_from_bytea", "lo_import", "lo_export", "lo_unlink", "pg_logical_emit_message",
        "pg_read_file", "pg_read_binary_file", "pg_ls_dir", "pg_terminate_backend", "pg_cancel_backend",
        "pg_advisory_lock", "pg_advisory_xact_lock", "set_config", "dblink_connect",
    ];

    const string NotSystem = "n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg\\_toast%' AND n.nspname NOT LIKE 'pg\\_temp%'";

    public static async Task<List<string>> TakeAsync(NpgsqlConnection superuser, string login)
    {
        var lines = new List<string>();
        async Task AddAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, superuser) { Parameters = { new() { Value = login }, new() { Value = BuiltIns } } };
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                lines.Add(reader.GetString(0));
        }

        // Who it is: attributes, memberships both ways, per-database settings, what it owns.
        await AddAsync("""
            SELECT format('attribute super=%s createrole=%s createdb=%s login=%s replication=%s bypassrls=%s inherit=%s',
                          rolsuper, rolcreaterole, rolcreatedb, rolcanlogin, rolreplication, rolbypassrls, rolinherit)
              FROM pg_roles WHERE rolname = $1
            """);
        await AddAsync("SELECT 'member of ' || m.roleid::regrole FROM pg_auth_members m WHERE m.member = $1::regrole");
        await AddAsync("SELECT 'has member ' || m.member::regrole FROM pg_auth_members m WHERE m.roleid = $1::regrole");
        await AddAsync("SELECT 'setting ' || CASE s.setdatabase WHEN 0 THEN '*' ELSE s.setdatabase::text END || ' ' || array_to_string(s.setconfig, ',') FROM pg_db_role_setting s WHERE s.setrole = $1::regrole");
        await AddAsync("SELECT 'owns ' || d.classid::regclass || ' ' || d.objid FROM pg_shdepend d WHERE d.refclassid = 'pg_authid'::regclass AND d.refobjid = $1::regrole AND d.deptype = 'o'");
        await AddAsync("SELECT 'owns large object ' || m.oid FROM pg_largeobject_metadata m WHERE m.lomowner = $1::regrole");
        // A default ACL makes every later table a grant nobody wrote (TST-4's plant), whoever it names.
        await AddAsync("SELECT 'default acl ' || d.defaclrole::regrole || ' ' || d.defaclobjtype::text || ' ' || coalesce(d.defaclnamespace::regnamespace::text, '*') || ' ' || d.defaclacl::text FROM pg_default_acl d");
        await AddAsync("SELECT 'server max_prepared_transactions=' || current_setting('max_prepared_transactions')");

        // What it can reach.
        await AddAsync("""
            SELECT format('database %s %s', CASE WHEN d.datname = current_database() THEN 'app' ELSE d.datname END, p)
              FROM pg_database d CROSS JOIN unnest(ARRAY['CONNECT', 'CREATE', 'TEMPORARY']) p
             WHERE (d.datname = current_database() OR d.datname IN ('postgres', 'template0', 'template1'))
               AND has_database_privilege($1, d.oid, p)
            """);
        await AddAsync("SELECT 'language ' || l.lanname FROM pg_language l WHERE has_language_privilege($1, l.oid, 'USAGE')");
        await AddAsync($"""
            SELECT format('schema %s %s', n.nspname, p)
              FROM pg_namespace n CROSS JOIN unnest(ARRAY['USAGE', 'CREATE']) p
             WHERE {NotSystem} AND has_schema_privilege($1, n.oid, p)
            """);
        await AddAsync($"""
            SELECT format('relation %s.%s %s', n.nspname, c.relname, p)
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             CROSS JOIN unnest(ARRAY['SELECT', 'INSERT', 'UPDATE', 'DELETE', 'TRUNCATE', 'REFERENCES', 'TRIGGER', 'MAINTAIN']) p
             WHERE {NotSystem} AND c.relkind IN ('r', 'p', 'v', 'm', 'f') AND has_table_privilege($1, c.oid, p)
            """);
        // A column privilege the table-level one does not already give.
        await AddAsync($"""
            SELECT format('column %s.%s.%s %s', n.nspname, c.relname, a.attname, p)
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
              JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
             CROSS JOIN unnest(ARRAY['SELECT', 'INSERT', 'UPDATE', 'REFERENCES']) p
             WHERE {NotSystem} AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
               AND has_column_privilege($1, c.oid, a.attnum, p) AND NOT has_table_privilege($1, c.oid, p)
            """);
        await AddAsync($"""
            SELECT format('sequence %s.%s %s', n.nspname, c.relname, p)
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             CROSS JOIN unnest(ARRAY['USAGE', 'SELECT', 'UPDATE']) p
             WHERE {NotSystem} AND c.relkind = 'S' AND has_sequence_privilege($1, c.oid, p)
            """);
        await AddAsync($"""
            SELECT 'function ' || p.oid::regprocedure
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE ({NotSystem} OR (n.nspname = 'pg_catalog' AND p.proname = ANY($2)))
               AND has_function_privilege($1, p.oid, 'EXECUTE')
            """);

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    /// <summary>The committed expected snapshot, one fact per line, blank lines and # comments ignored.</summary>
    public static List<string> Expected(string login) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "ExpectedPrivileges", login + ".txt"))
            .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).Order(StringComparer.Ordinal).ToList();

    /// <summary>A diff a failing assertion can print: what is there and should not be, and the reverse.</summary>
    public static string Diff(IReadOnlyCollection<string> expected, IReadOnlyCollection<string> actual) =>
        string.Join(Environment.NewLine,
            actual.Except(expected).Select(l => "+ " + l).Concat(expected.Except(actual).Select(l => "- " + l)));
}
