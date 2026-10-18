-- Spike G, part 4: the ruled rule, asserted. Each case is loaded in BOTH insertion orders
-- and read under BOTH plans; any answer other than the expected one raises.

\set ON_ERROR_STOP on
set search_path to public;
\o /dev/null

create or replace function pg_temp.ins(d date, amt numeric, src text, strength source_strength, seen timestamptz)
returns void language sql as $$
    insert into account_balance (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at)
    values ('aaaaaaaa-0000-4000-8000-0000000000a1', d, amt, 'USD', src, strength, seen);
$$;

create or replace function pg_temp.check(label text, d date, expected numeric) returns void language plpgsql as $$
declare got numeric; plan text;
begin
    foreach plan in array array['default', 'seqscan'] loop
        if plan = 'seqscan' then
            perform set_config('enable_indexscan', 'off', true);
            perform set_config('enable_bitmapscan', 'off', true);
        end if;
        select net_worth into got from v_net_worth_daily where as_of_date = d;
        if got is distinct from expected then
            raise exception 'FAIL %: % under % plan, expected %, got %', label, d, plan, expected, got;
        end if;
        perform set_config('enable_indexscan', 'on', true);
        perform set_config('enable_bitmapscan', 'on', true);
    end loop;
    raise notice 'PASS %: % = % under both plans', label, d, expected;
end $$;

-- G1. Same date, manual (100) vs export (200): the export counts, whichever came first.
begin;
select pg_temp.ins('2026-03-01', 100, 'manual', 'manual', '2026-03-01 09:00Z');
select pg_temp.ins('2026-03-01', 200, 'import:csv', 'export', '2026-03-01 08:00Z');
analyze account_balance;
select pg_temp.check('G1 manual first', '2026-03-01', 200);
rollback;
begin;
select pg_temp.ins('2026-03-01', 200, 'import:csv', 'export', '2026-03-01 08:00Z');
select pg_temp.ins('2026-03-01', 100, 'manual', 'manual', '2026-03-01 09:00Z');
analyze account_balance;
select pg_temp.check('G1 export first', '2026-03-01', 200);
rollback;

-- G2. A newer manual figure outranks an older export: strength never reaches back past a
-- newer date. On 03-01 only the export exists; on 03-02 the manual one is newest.
begin;
select pg_temp.ins('2026-03-01', 200, 'import:csv', 'export', '2026-03-01 08:00Z');
select pg_temp.ins('2026-03-02', 150, 'manual', 'manual', '2026-03-02 09:00Z');
analyze account_balance;
select pg_temp.check('G2 older export', '2026-03-01', 200);
select pg_temp.check('G2 newer manual', '2026-03-02', 150);
select pg_temp.check('G2 carried manual', '2026-03-05', 150);
rollback;

-- G3. Same date, same strength from two sources (CSV 300, OFX 400): the later observation
-- counts, whichever was inserted first.
begin;
select pg_temp.ins('2026-03-01', 300, 'import:csv', 'export', '2026-03-03 08:00Z');
select pg_temp.ins('2026-03-01', 400, 'import:ofx', 'export', '2026-03-04 08:00Z');
analyze account_balance;
select pg_temp.check('G3 csv first', '2026-03-01', 400);
rollback;
begin;
select pg_temp.ins('2026-03-01', 400, 'import:ofx', 'export', '2026-03-04 08:00Z');
select pg_temp.ins('2026-03-01', 300, 'import:csv', 'export', '2026-03-03 08:00Z');
analyze account_balance;
select pg_temp.check('G3 ofx first', '2026-03-01', 400);
rollback;

\o
\echo '=== spike G: all assertions passed ==='
