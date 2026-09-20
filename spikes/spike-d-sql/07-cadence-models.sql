-- Spike D, part 7: the cutoff under Kyle's stated cadence, and under two
-- different ENTRY PATTERNS.
--
-- 06 assumed monthly entry with each account drifting independently. Two
-- things were wrong with that:
--
--   1. The stated cadence is "monthly, realistically every six weeks", so the
--      median gap is ~42 days, not ~33. A 45-day cutoff sits ON the median,
--      which is the worst possible place to put it.
--   2. Accounts do not drift independently if you enter them in one sitting.
--      "Doing the finances" is a BATCH, and a batch makes every account share
--      an as_of_date -- which means staleness is CORRELATED, and the failure
--      is not "2 of 25 go stale" but "all 25 expire on the same day and the
--      net worth goes NULL".
--
-- Pattern B is the one that decides the number, and 06 could not see it.

\set ON_ERROR_STOP on

drop schema if exists cadence cascade;
create schema cadence;
set search_path to cadence, public;

create table entry (pattern text not null, account_id int not null, as_of_date date not null);
select setseed(0.42);

-- Gap model, stated so it can be argued with: "monthly intent, six-week
-- reality."  45% inside six weeks | 35% six-to-nine weeks | 15% skipped |
-- 5% long gap.  Target median ~42 days.
create function next_gap() returns int language sql volatile as $$
    select case
             when random() < 0.45 then 28 + (random() * 14)::int
             when random() < 0.64 then 43 + (random() * 17)::int
             when random() < 0.91 then 61 + (random() * 29)::int
             else                      91 + (random() * 59)::int
           end;
$$;

do $$
declare acct int; d date; g int;
begin
    -- PATTERN A: every account drifts on its own schedule.
    for acct in 1..25 loop
        d := date '2024-01-01' + (random() * 42)::int;
        while d < date '2027-01-01' loop
            insert into entry values ('A: independent', acct, d);
            d := d + next_gap();
        end loop;
    end loop;

    -- PATTERN B: one sitting. Every account gets the same as_of_date.
    d := date '2024-01-01';
    while d < date '2027-01-01' loop
        insert into entry select 'B: batch', a, d from generate_series(1,25) a;
        d := d + next_gap();
    end loop;

    -- PATTERN C: a batch of 20, plus 5 accounts that get done when they get
    -- done. The realistic one -- the awkward accounts always lag.
    d := date '2024-01-01';
    while d < date '2027-01-01' loop
        insert into entry select 'C: batch + 5 stragglers', a, d from generate_series(1,20) a;
        d := d + next_gap();
    end loop;
    for acct in 21..25 loop
        d := date '2024-01-01' + (random() * 42)::int;
        while d < date '2027-01-01' loop
            insert into entry values ('C: batch + 5 stragglers', acct, d);
            d := d + next_gap() + 30;   -- the stragglers lag by about a month
        end loop;
    end loop;
end;
$$;

create table spine as
select d::date as as_of_date
  from generate_series(date '2024-05-01', date '2026-12-31', interval '1 day') d;

create function metrics(p text, n int)
returns table (pct_all_fresh numeric, mean_stale numeric, pct_days_null numeric)
language sql stable as $$
    with per_day as (
        select s.as_of_date, count(*) filter (where e.as_of_date is null) as stale
          from spine s
          cross join generate_series(1,25) a(id)
          left join lateral (
              select e.as_of_date from entry e
               where e.pattern = p and e.account_id = a.id
                 and e.as_of_date <= s.as_of_date
                 and e.as_of_date >= s.as_of_date - n
               order by e.as_of_date desc limit 1
          ) e on true
         group by s.as_of_date)
    select round(100.0 * count(*) filter (where stale = 0) / count(*), 1),
           round(avg(stale), 2),
           -- every account stale => sum() is NULL => the chart has no number
           round(100.0 * count(*) filter (where stale = 25) / count(*), 1)
      from per_day;
$$;

\echo ''
\echo '=== gap distribution generated (target: median ~42d, "monthly / really six weeks") ==='
with g as (select as_of_date - lag(as_of_date) over (partition by pattern, account_id order by as_of_date) as gap
             from entry where pattern = 'A: independent')
select round(avg(gap),1) as mean,
       percentile_disc(0.5) within group (order by gap) as p50,
       percentile_disc(0.9) within group (order by gap) as p90,
       max(gap) as max
  from g where gap is not null;

\echo ''
\echo '=== %ALL FRESH / avg stale of 25 / %DAYS WITH NO NUMBER AT ALL ==='
\echo ''
select c.n as cutoff,
       (select pct_all_fresh from metrics('A: independent', c.n))          as "A %fresh",
       (select mean_stale    from metrics('A: independent', c.n))          as "A avg stale",
       (select pct_all_fresh from metrics('B: batch', c.n))                as "B %fresh",
       (select pct_days_null from metrics('B: batch', c.n))                as "B %NO NUMBER",
       (select pct_all_fresh from metrics('C: batch + 5 stragglers', c.n)) as "C %fresh",
       (select pct_days_null from metrics('C: batch + 5 stragglers', c.n)) as "C %NO NUMBER"
  from (values (45),(60),(75),(90),(105),(120),(150),(180)) c(n)
 order by c.n;
