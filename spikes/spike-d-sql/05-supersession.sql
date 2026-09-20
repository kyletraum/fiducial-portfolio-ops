-- Spike D, part 4: the restatement write order.
--
-- M-9. Manual entry means a balance WILL be restated, and the partial unique
-- index means the two statements that do it are order-dependent. Established
-- already; this file exists to show it failing, because "in this order" is the
-- kind of instruction that reads like style until you have watched the other
-- order raise.
--
--   RIGHT:  UPDATE old SET superseded_at = now();   then INSERT new
--   WRONG:  INSERT new;                             then UPDATE old
--
-- The wrong order leaves both rows live for the instant between the two
-- statements, and the index is checked at statement time, not at commit.
-- In EF Core this is an explicit transaction with ExecuteUpdate then Add --
-- NOT one SaveChanges, whose statement order you do not control.

\set ON_ERROR_STOP on
set search_path to spike, public;

\echo '--- 4a. correct order: UPDATE old, then INSERT new ---'

begin;

update account_balance
   set superseded_at = timestamptz '2026-03-11 10:00+00'
 where id = 'bbbbbbbb-0000-4000-8000-000000000001';

insert into account_balance
    (id, account_id, as_of_date, balance, currency, source_system, source_strength,
     observed_at, supersedes)
values
    ('bbbbbbbb-0000-4000-8000-00000000000a', 'aaaaaaaa-0000-4000-8000-000000000001',
     date '2026-03-10', 110.0000, 'USD', 'manual', 'manual',
     timestamptz '2026-03-11 10:00+00', 'bbbbbbbb-0000-4000-8000-000000000001');

commit;

\echo 'correct order committed. live + superseded rows for account A on 2026-03-10:'
select balance,
       superseded_at is not null as superseded,
       supersedes is not null    as supersedes_a_row
  from account_balance
 where account_id = 'aaaaaaaa-0000-4000-8000-000000000001'
   and as_of_date = date '2026-03-10'
 order by created_at;

\echo ''
\echo '--- 4b. reversed order: INSERT new, then UPDATE old (expected to FAIL) ---'

-- ON_ERROR_STOP would abort the script, so the failing statement runs inside a
-- DO block that catches and reports it. The failure is the result.
do $$
begin
    begin
        insert into account_balance
            (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at)
        values
            ('aaaaaaaa-0000-4000-8000-000000000002', date '2026-03-10', 60.0000, 'USD',
             'manual', 'manual', timestamptz '2026-03-11 10:00+00');

        update account_balance
           set superseded_at = now()
         where id = 'bbbbbbbb-0000-4000-8000-000000000002';

        raise exception 'SPIKE FAILED: reversed order was accepted, which contradicts M-9';
    exception when unique_violation then
        raise notice 'reversed order rejected as expected: % (%)', sqlerrm, sqlstate;
    end;
end;
$$;

\echo 'account B is unchanged -- the reversed order changed nothing:'
select balance, superseded_at is not null as superseded
  from account_balance
 where account_id = 'aaaaaaaa-0000-4000-8000-000000000002'
 order by created_at;

\echo ''
\echo '--- 4c. the restatement reaches the chart ---'
do $$
declare r record;
begin
    select * into r from v_net_worth_daily where as_of_date = date '2026-03-10';
    -- 150.0000 before the restatement, 160.0000 after it, from one live row
    -- per (account, date, source) and no double count.
    perform expect('2026-03-10 net_worth after restatement', r.net_worth::text, '160.0000');
    perform expect('2026-03-10 accounts_verified after restatement', r.accounts_verified::text, '2');
end;
$$;
