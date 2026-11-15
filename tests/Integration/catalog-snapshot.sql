-- slice-04.md step 1a, and step 6 by hand: everything a login can do, read from the catalog as
-- EFFECTIVE privileges (has_*_privilege), so a grant through PUBLIC or a role membership counts as a
-- grant (R2-DBS-3). One line per fact. Compared with tests/Integration/ExpectedPrivileges/<login>.txt.
--
-- The ONE query, run two ways so they cannot drift (R2-TST-11):
--   CI:      CatalogSnapshot.TakeAsync, after set_config('snapshot.login', <login>, false);
--   by hand: docker exec -i <postgres> psql -U postgres -d portfolio_dev -At -v ON_ERROR_STOP=1 \
--              -c "SET snapshot.login = 'portfolio_api'" -f - < tests/Integration/catalog-snapshot.sql
--
-- Databases are named by role ('app' is the current one), so one expected file serves both.
-- The named built-ins are the ones slice 04 revokes, and the ones a reader would ask about;
-- pg_catalog as a whole is PUBLIC's by design (SCO-13).
WITH login AS (
  SELECT current_setting('snapshot.login') AS name
), builtin AS (
  SELECT ARRAY['lo_create', 'lo_creat', 'lo_from_bytea', 'lo_import', 'lo_export', 'lo_unlink', 'pg_logical_emit_message',
               'pg_read_file', 'pg_read_binary_file', 'pg_ls_dir', 'pg_terminate_backend', 'pg_cancel_backend',
               'pg_advisory_lock', 'pg_advisory_xact_lock', 'set_config', 'dblink_connect'] AS names
), ns AS (
  SELECT n.oid, n.nspname FROM pg_namespace n
   WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg\_toast%' AND n.nspname NOT LIKE 'pg\_temp%'
), facts AS (
  -- Who it is: attributes, memberships both ways, per-database settings, what it owns.
  SELECT format('attribute super=%s createrole=%s createdb=%s login=%s replication=%s bypassrls=%s inherit=%s',
                rolsuper, rolcreaterole, rolcreatedb, rolcanlogin, rolreplication, rolbypassrls, rolinherit) AS line
    FROM pg_roles, login WHERE rolname = login.name
  UNION ALL
  SELECT 'member of ' || m.roleid::regrole FROM pg_auth_members m, login WHERE m.member = login.name::regrole
  UNION ALL
  SELECT 'has member ' || m.member::regrole FROM pg_auth_members m, login WHERE m.roleid = login.name::regrole
  UNION ALL
  SELECT 'setting ' || CASE s.setdatabase WHEN 0 THEN '*' ELSE s.setdatabase::text END || ' ' || array_to_string(s.setconfig, ',')
    FROM pg_db_role_setting s, login WHERE s.setrole = login.name::regrole
  UNION ALL
  SELECT 'owns ' || d.classid::regclass || ' ' || d.objid
    FROM pg_shdepend d, login WHERE d.refclassid = 'pg_authid'::regclass AND d.refobjid = login.name::regrole AND d.deptype = 'o'
  UNION ALL
  SELECT 'owns large object ' || m.oid FROM pg_largeobject_metadata m, login WHERE m.lomowner = login.name::regrole
  UNION ALL
  -- A default ACL makes every later table a grant nobody wrote (TST-4's plant), whoever it names.
  SELECT 'default acl ' || d.defaclrole::regrole || ' ' || d.defaclobjtype::text || ' '
         || coalesce(d.defaclnamespace::regnamespace::text, '*') || ' ' || d.defaclacl::text
    FROM pg_default_acl d
  UNION ALL
  SELECT 'server max_prepared_transactions=' || current_setting('max_prepared_transactions')
  UNION ALL
  -- What it can reach.
  SELECT format('database %s %s', CASE WHEN d.datname = current_database() THEN 'app' ELSE d.datname END, p)
    FROM pg_database d CROSS JOIN unnest(ARRAY['CONNECT', 'CREATE', 'TEMPORARY']) p, login
   WHERE (d.datname = current_database() OR d.datname IN ('postgres', 'template0', 'template1'))
     AND has_database_privilege(login.name, d.oid, p)
  UNION ALL
  SELECT 'language ' || l.lanname FROM pg_language l, login WHERE has_language_privilege(login.name, l.oid, 'USAGE')
  UNION ALL
  SELECT format('schema %s %s', ns.nspname, p)
    FROM ns CROSS JOIN unnest(ARRAY['USAGE', 'CREATE']) p, login
   WHERE has_schema_privilege(login.name, ns.oid, p)
  UNION ALL
  SELECT format('relation %s.%s %s', ns.nspname, c.relname, p)
    FROM pg_class c JOIN ns ON ns.oid = c.relnamespace
   CROSS JOIN unnest(ARRAY['SELECT', 'INSERT', 'UPDATE', 'DELETE', 'TRUNCATE', 'REFERENCES', 'TRIGGER', 'MAINTAIN']) p, login
   WHERE c.relkind IN ('r', 'p', 'v', 'm', 'f') AND has_table_privilege(login.name, c.oid, p)
  UNION ALL
  -- A column privilege the table-level one does not already give.
  SELECT format('column %s.%s.%s %s', ns.nspname, c.relname, a.attname, p)
    FROM pg_class c JOIN ns ON ns.oid = c.relnamespace
    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
   CROSS JOIN unnest(ARRAY['SELECT', 'INSERT', 'UPDATE', 'REFERENCES']) p, login
   WHERE c.relkind IN ('r', 'p', 'v', 'm', 'f')
     AND has_column_privilege(login.name, c.oid, a.attnum, p) AND NOT has_table_privilege(login.name, c.oid, p)
  UNION ALL
  SELECT format('sequence %s.%s %s', ns.nspname, c.relname, p)
    FROM pg_class c JOIN ns ON ns.oid = c.relnamespace
   CROSS JOIN unnest(ARRAY['USAGE', 'SELECT', 'UPDATE']) p, login
   WHERE c.relkind = 'S' AND has_sequence_privilege(login.name, c.oid, p)
  UNION ALL
  SELECT 'function ' || p.oid::regprocedure
    FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace, login, builtin
   WHERE (p.pronamespace IN (SELECT oid FROM ns) OR (n.nspname = 'pg_catalog' AND p.proname = ANY(builtin.names)))
     AND has_function_privilege(login.name, p.oid, 'EXECUTE')
)
SELECT line FROM facts ORDER BY line COLLATE "C";
