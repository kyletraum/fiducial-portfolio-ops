-- Spike G, part 3: the tie-break Kyle ruled on 2026-10-18 (slice-02.md, Spike G).
--   1. newest as_of_date first - a stronger source never reaches back past a newer date;
--   2. on the same date, strongest source_strength first (the enum is declared strongest
--      first, so ASC);
--   3. then newest observed_at;
--   4. then id - not part of the ruling, only so the order is TOTAL and no tie is left for
--      the planner to break.
-- The view is otherwise byte-for-byte migration Initial's; only the lateral's ORDER BY moves.

\set ON_ERROR_STOP on
set search_path to public;

create or replace view v_net_worth_daily as
select
    s.as_of_date,
    sum(o.balance)                                       as net_worth,
    count(*)                                             as accounts_in_window,
    count(o.balance)                                     as accounts_verified,
    count(*) - count(o.balance)                          as accounts_unverified,
    count(*) filter (where o.as_of_date < s.as_of_date)  as accounts_carried,
    max(s.as_of_date - o.as_of_date)                     as max_staleness_days
from v_date_spine s
join account a
  on a.deleted_at is null
 and s.as_of_date >= a.opened_on
 and s.as_of_date <= coalesce(a.closed_on, 'infinity'::date)
left join lateral (
    select b.balance, b.as_of_date
      from account_balance b
     where b.account_id = a.id
       and b.deleted_at is null
       and b.superseded_at is null
       and b.as_of_date <= s.as_of_date
       and b.as_of_date >= s.as_of_date
                           - (select balance_staleness_days from app_setting)
     order by b.as_of_date desc, b.source_strength asc, b.observed_at desc, b.id desc
     limit 1
) o on true
group by s.as_of_date;
