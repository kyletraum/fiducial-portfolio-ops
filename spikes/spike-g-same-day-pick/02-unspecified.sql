-- Spike G, part 2: the view as migration Initial wrote it. Two live rows for one account on
-- one date - a manual entry (100) and an export (200), different source_system, so the live
-- index allows both. The lateral's ORDER BY b.as_of_date DESC LIMIT 1 ties them.
--
-- Nothing is asserted here: this file RECORDS what the unfixed view returns, under two
-- insertion orders and two plans. If every line says the same number, the claim that the
-- pick is observable is wrong, and RESULTS.md says so.

\set ON_ERROR_STOP on
set search_path to public;

create temporary table observed (insertion text, plan text, net_worth numeric);

create or replace function pg_temp.load(first_source text) returns void language plpgsql as $$
begin
    delete from account_balance;
    if first_source = 'manual' then
        insert into account_balance (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at)
        values ('aaaaaaaa-0000-4000-8000-0000000000a1', date '2026-03-01', 100, 'USD', 'manual', 'manual', timestamptz '2026-03-01 09:00Z');
        insert into account_balance (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at)
        values ('aaaaaaaa-0000-4000-8000-0000000000a1', date '2026-03-01', 200, 'USD', 'import:csv', 'export', timestamptz '2026-03-01 08:00Z');
    else
        insert into account_balance (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at)
        values ('aaaaaaaa-0000-4000-8000-0000000000a1', date '2026-03-01', 200, 'USD', 'import:csv', 'export', timestamptz '2026-03-01 08:00Z');
        insert into account_balance (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at)
        values ('aaaaaaaa-0000-4000-8000-0000000000a1', date '2026-03-01', 100, 'USD', 'manual', 'manual', timestamptz '2026-03-01 09:00Z');
    end if;
end $$;

\o /dev/null
-- manual inserted first
select pg_temp.load('manual');
analyze account_balance;
insert into observed select 'manual first', 'default', net_worth from v_net_worth_daily where as_of_date = '2026-03-01';
set enable_indexscan = off; set enable_bitmapscan = off;
insert into observed select 'manual first', 'seqscan', net_worth from v_net_worth_daily where as_of_date = '2026-03-01';
reset enable_indexscan; reset enable_bitmapscan;

-- export inserted first
select pg_temp.load('export');
analyze account_balance;
insert into observed select 'export first', 'default', net_worth from v_net_worth_daily where as_of_date = '2026-03-01';
set enable_indexscan = off; set enable_bitmapscan = off;
insert into observed select 'export first', 'seqscan', net_worth from v_net_worth_daily where as_of_date = '2026-03-01';
reset enable_indexscan; reset enable_bitmapscan;

\o
\echo 'unfixed view, net worth on 2026-03-01 (manual = 100, export = 200):'
select insertion, plan, net_worth from observed order by insertion desc, plan;
select count(distinct net_worth) as distinct_answers from observed;

\o /dev/null
delete from account_balance;
\o
