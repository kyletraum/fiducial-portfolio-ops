-- Spike D, part 3: v_net_worth_daily, and the two wrong versions of it.
--
-- M-1 / Amendment 3. Accounts do not report on the same days, so a per-date
-- sum over only the rows that exist sums a different subset every day. The
-- fix is carry-forward WITHIN each account's active window, with every carried
-- point marked as carried.
--
-- Three structural rules, none of them stylistic:
--
--   1. LEFT JOIN LATERAL ... ON true, never CROSS JOIN LATERAL. Under CROSS
--      JOIN an account with no observation produces no row at all, so
--      count(*) - count(balance) counts nothing and is identically zero -- the
--      unverified count silently reports the opposite of the truth.
--   2. The lateral is ORDER BY as_of_date DESC LIMIT 1, not DISTINCT ON:
--      DISTINCT ON yields one row per account, not a daily series.
--   3. sum() over all-NULLs is NULL, not 0. That is correct and must be kept:
--      Constitution II says a day with nothing verified is UNVERIFIED. The
--      consumers branch on it; the view does not coalesce it away.

\set ON_ERROR_STOP on
set search_path to spike, public;

-- The date spine. Bounded by the data so the spike is deterministic; the
-- series endpoint clamps the upper bound at current_date instead.
create view v_date_spine as
select d::date as as_of_date
  from generate_series(
        -- The spine starts when the earliest account OPENED, not at the first
        -- observation: days on which accounts existed and nothing was entered
        -- are days the chart must show as UNVERIFIED (Constitution II), not
        -- days the chart may omit. The series endpoint passes an explicit
        -- from/to instead of inheriting this unbounded one.
        (select min(opened_on) from account where deleted_at is null),
        greatest(
          (select max(as_of_date) from account_balance
            where deleted_at is null and superseded_at is null)
            + (select balance_staleness_days from app_setting),
          (select max(closed_on) from account where deleted_at is null)),
        interval '1 day') as d;

create view v_net_worth_daily as
select
    s.as_of_date,
    -- NULL, not 0, when nothing in window is verified. Deliberate.
    sum(o.balance)                                             as net_worth,
    count(*)                                                   as accounts_in_window,
    count(o.balance)                                           as accounts_verified,
    -- R2-B6. Only non-zero because the join above is a LEFT JOIN.
    count(*) - count(o.balance)                                as accounts_unverified,
    count(*) filter (where o.as_of_date < s.as_of_date)        as accounts_carried,
    max(s.as_of_date - o.as_of_date)                           as max_staleness_days
from v_date_spine s
-- Membership, i.e. the denominator: the account's own calendar window. An
-- account that is open but has never reported is IN this set and contributes
-- NULL -- that is the whole point of accounts_unverified.
join account a
  on a.deleted_at is null
 and s.as_of_date >= a.opened_on
 and s.as_of_date <= coalesce(a.closed_on, 'infinity'::date)
-- Carry-forward, i.e. the numerator. Three bounds, all load-bearing:
--   as_of_date <= s.as_of_date  -- never read the future
--   as_of_date >= s - staleness -- Amendment 3 condition 2: the carry EXPIRES,
--                                  so a silently-stopped account is not filled
--                                  forever. Past the cutoff it goes unverified,
--                                  which is Principle II, not Principle III's
--                                  forbidden fill.
-- and membership above stops it at closed_on, which is Principle III proper:
-- past a series end, no point is invented at all.
left join lateral (
    select b.balance, b.as_of_date
      from account_balance b
     where b.account_id = a.id
       and b.deleted_at is null
       and b.superseded_at is null
       and b.as_of_date <= s.as_of_date
       and b.as_of_date >= s.as_of_date
                           - (select balance_staleness_days from app_setting)
     order by b.as_of_date desc
     limit 1
) o on true
group by s.as_of_date;

-- ---------------------------------------------------------------------------
-- The wrong version, kept so the assertion can compare against it rather than
-- assert a property nobody can see fail. Identical in every respect except the
-- join word.
create view v_net_worth_daily_crossjoin as
select
    s.as_of_date,
    sum(o.balance)              as net_worth,
    count(*)                    as accounts_in_window,
    count(o.balance)            as accounts_verified,
    count(*) - count(o.balance) as accounts_unverified
from v_date_spine s
join account a
  on a.deleted_at is null
 and s.as_of_date >= a.opened_on
 and s.as_of_date <= coalesce(a.closed_on, 'infinity'::date)
cross join lateral (
    select b.balance, b.as_of_date
      from account_balance b
     where b.account_id = a.id
       and b.deleted_at is null
       and b.superseded_at is null
       and b.as_of_date <= s.as_of_date
       and b.as_of_date >= s.as_of_date
                           - (select balance_staleness_days from app_setting)
     order by b.as_of_date desc
     limit 1
) o
group by s.as_of_date;
