-- Spike D, part 6: what does the staleness cutoff actually cost?
--
-- Amendment 3 condition 2 requires a cutoff and does not name one. Picking it
-- by feel is how a number nobody can defend ends up load-bearing, so this
-- measures the two errors the cutoff trades against each other:
--
--   FALSE ALARM -- a live account that is merely late reads as unverified, so
--                  the chart degrades for no reason. Grows with N shrinking,
--                  and grows fast with the number of accounts.
--   THE LIE     -- an account that has silently died keeps contributing its
--                  last value. Lasts exactly N days, by construction.
--
-- The entry-cadence model below is an ASSUMPTION about human behaviour, not a
-- measurement. It is stated here so the number it produces can be argued with.

\set ON_ERROR_STOP on

drop schema if exists cutoff cascade;
create schema cutoff;
set search_path to cutoff, public;

create table entry (account_id int not null, as_of_date date not null);

-- Deterministic: same numbers on every run, on any machine.
select setseed(0.42);

-- "Conscientious but human" monthly entry, over three years:
--   70%  28-35 days  -- on time
--   20%  36-50 days  -- late
--    8%  51-75 days  -- skipped a month
--    2%  76-120 days -- skipped two
do $$
declare
    acct int;
    d    date;
    roll numeric;
    gap  int;
begin
    for acct in 1..25 loop
        -- stagger the start so accounts are not all entered on the same day
        d := date '2024-01-01' + (random() * 30)::int;
        while d < date '2027-01-01' loop
            insert into entry values (acct, d);
            roll := random();
            gap := case
                     when roll < 0.70 then 28 + (random() * 7)::int
                     when roll < 0.90 then 36 + (random() * 14)::int
                     when roll < 0.98 then 51 + (random() * 24)::int
                     else                  76 + (random() * 44)::int
                   end;
            d := d + gap;
        end loop;
    end loop;
end;
$$;

create table spine as
select d::date as as_of_date
  from generate_series(date '2024-04-01', date '2026-12-31', interval '1 day') d;
-- starts three months in, so every account already has a first observation and
-- the warm-up does not pollute the measurement

-- For a given cutoff N and a given account count, how often is the chart
-- clean? Same LEFT JOIN LATERAL shape as v_net_worth_daily -- the metric is
-- measured through the real logic, not a restatement of it.
create function metrics(n int, n_accounts int)
returns table (cutoff_days int, accounts int, pct_days_all_fresh numeric, mean_stale numeric, worst_day int)
language sql stable as $$
    with per_day as (
        select s.as_of_date,
               count(*) filter (where e.as_of_date is null) as stale
          from spine s
          cross join generate_series(1, n_accounts) a(id)
          left join lateral (
              select e.as_of_date
                from entry e
               where e.account_id = a.id
                 and e.as_of_date <= s.as_of_date
                 and e.as_of_date >= s.as_of_date - n
               order by e.as_of_date desc
               limit 1
          ) e on true
         group by s.as_of_date
    )
    select n, n_accounts,
           round(100.0 * count(*) filter (where stale = 0) / count(*), 1),
           round(avg(stale), 2),
           max(stale)
      from per_day;
$$;

\echo ''
\echo '=== how often is the chart FULLY verified, by cutoff and account count ==='
\echo '(pct_days_all_fresh = share of days on which no live account reads stale)'
\echo ''

select m.cutoff_days,
       max(case when m.accounts = 5  then m.pct_days_all_fresh end) as "5 accts %clean",
       max(case when m.accounts = 5  then m.mean_stale end)         as "5 accts avg stale",
       max(case when m.accounts = 25 then m.pct_days_all_fresh end) as "25 accts %clean",
       max(case when m.accounts = 25 then m.mean_stale end)         as "25 accts avg stale",
       max(case when m.accounts = 25 then m.worst_day end)          as "25 accts worst day"
  from (values (21),(30),(35),(45),(60),(75),(90),(120)) c(n)
  cross join (values (5),(25)) k(n_accounts)
  cross join lateral metrics(c.n, k.n_accounts) m
 group by m.cutoff_days
 order by m.cutoff_days;

\echo ''
\echo '=== the other side: a silently-dead account keeps contributing for N days ==='
\echo '(exactly N, by construction -- the cutoff IS the maximum duration of the lie)'
