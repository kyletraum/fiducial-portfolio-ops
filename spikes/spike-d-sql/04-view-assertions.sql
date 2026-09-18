-- Spike D, part 4: what the view actually returns.
--
-- Assertions, not a printout. Every one of these raises and aborts the spike
-- if the number moves, so this file is the thing that will notice when the
-- view is rewritten in step 2 and a property is lost.

\set ON_ERROR_STOP on
set search_path to spike, public;

create function expect(label text, actual text, expected text) returns void
language plpgsql as $$
begin
    if actual is not distinct from expected then
        raise notice 'PASS  % = %', label, coalesce(actual, 'NULL');
    else
        raise exception 'FAIL  %: expected %, got %',
            label, coalesce(expected, 'NULL'), coalesce(actual, 'NULL');
    end if;
end;
$$;

\echo '--- 4.1  the measurement date: 4 accounts in window, 2 of them observed ---'
do $$
declare r record;
begin
    select * into r from v_net_worth_daily where as_of_date = date '2026-03-10';
    perform expect('2026-03-10 net_worth',          r.net_worth::text,           '150.0000');
    perform expect('2026-03-10 accounts_in_window', r.accounts_in_window::text,  '4');
    perform expect('2026-03-10 accounts_verified',  r.accounts_verified::text,   '2');
    perform expect('2026-03-10 accounts_unverified', r.accounts_unverified::text, '2');
    perform expect('2026-03-10 accounts_carried',   r.accounts_carried::text,    '0');
    perform expect('2026-03-10 max_staleness_days', r.max_staleness_days::text,  '0');
end;
$$;

\echo ''
\echo '--- 4.2  the same query with CROSS JOIN LATERAL: unverified collapses to 0 ---'
do $$
declare r record;
begin
    select * into r from v_net_worth_daily_crossjoin where as_of_date = date '2026-03-10';
    -- Same headline number. That is what makes it dangerous: the total is
    -- right and the count that exists to qualify it is silently wrong.
    perform expect('CROSS JOIN net_worth',           r.net_worth::text,           '150.0000');
    perform expect('CROSS JOIN accounts_in_window',  r.accounts_in_window::text,  '2');
    perform expect('CROSS JOIN accounts_unverified', r.accounts_unverified::text, '0');
end;
$$;

\echo ''
\echo '--- 4.3  a day where nothing in window is verified: sum() is NULL, not 0 ---'
do $$
declare r record;
begin
    select * into r from v_net_worth_daily where as_of_date = date '2026-01-05';
    perform expect('2026-01-05 net_worth',           r.net_worth::text,           null);
    perform expect('2026-01-05 accounts_in_window',  r.accounts_in_window::text,  '4');
    perform expect('2026-01-05 accounts_unverified', r.accounts_unverified::text, '4');
    perform expect('2026-01-05 max_staleness_days',  r.max_staleness_days::text,  null);
end;
$$;

\echo ''
\echo '--- 4.4  a carried point is marked as carried, with its staleness ---'
do $$
declare r record;
begin
    select * into r from v_net_worth_daily where as_of_date = date '2026-03-11';
    perform expect('2026-03-11 net_worth',          r.net_worth::text,          '150.0000');
    perform expect('2026-03-11 accounts_carried',   r.accounts_carried::text,   '2');
    perform expect('2026-03-11 max_staleness_days', r.max_staleness_days::text, '1');
end;
$$;

\echo ''
\echo '--- 4.5  the carry EXPIRES at the cutoff (Amendment 3, condition 2) ---'
do $$
declare last_day record; first_day_after record;
begin
    -- A and B were observed on 2026-03-10; 45 days later is 2026-04-24.
    select * into last_day        from v_net_worth_daily where as_of_date = date '2026-04-24';
    select * into first_day_after from v_net_worth_daily where as_of_date = date '2026-04-25';

    perform expect('2026-04-24 net_worth',           last_day.net_worth::text,           '350.0000');
    perform expect('2026-04-24 accounts_unverified', last_day.accounts_unverified::text, '1');
    perform expect('2026-04-24 max_staleness_days',  last_day.max_staleness_days::text,  '45');

    -- One day later the two stalest accounts drop out of the numerator and
    -- into the unverified count. They do NOT drop out of the denominator:
    -- they are open accounts we have not verified (Principle II), not ended
    -- series (Principle III).
    perform expect('2026-04-25 net_worth',            first_day_after.net_worth::text,            '200.0000');
    perform expect('2026-04-25 accounts_in_window',   first_day_after.accounts_in_window::text,   '4');
    perform expect('2026-04-25 accounts_unverified',  first_day_after.accounts_unverified::text,  '3');
end;
$$;

\echo ''
\echo '--- 4.6  a closed account stops (Principle III: no fill past a series end) ---'
do $$
declare on_close record; after_close record; e_rows bigint;
begin
    select * into on_close    from v_net_worth_daily where as_of_date = date '2026-06-30';
    select * into after_close from v_net_worth_daily where as_of_date = date '2026-07-01';

    -- Account E is the only verified account left; the other four have gone
    -- stale. The headline is 25 and the row says 4 of 5 are unverified.
    perform expect('2026-06-30 net_worth',           on_close.net_worth::text,           '25.0000');
    perform expect('2026-06-30 accounts_in_window',  on_close.accounts_in_window::text,  '5');
    perform expect('2026-06-30 accounts_unverified', on_close.accounts_unverified::text, '4');

    -- The day after closed_on, E is gone from the denominator entirely. Its
    -- 25.0000 is not carried, not zeroed, not interpolated -- absent.
    perform expect('2026-07-01 accounts_in_window',  after_close.accounts_in_window::text, '4');
    perform expect('2026-07-01 net_worth',           after_close.net_worth::text,          null);

    -- and the spine does extend past the close, so the assertion above is a
    -- real absence rather than a missing row.
    select count(*) into e_rows from v_date_spine where as_of_date > date '2026-06-30';
    if e_rows = 0 then
        raise exception 'FAIL  the spine stops at closed_on, so 4.6 proves nothing';
    end if;
    raise notice 'PASS  spine extends % days past closed_on', e_rows;
end;
$$;

\echo ''
\echo '--- 4.7  DISTINCT ON cannot express this (it is rejected, and it is the wrong shape anyway) ---'
do $$
begin
    begin
        execute $q$
            select distinct on (account_id) account_id, balance
              from account_balance
             where deleted_at is null and superseded_at is null
             order by as_of_date desc
        $q$;
        raise exception 'SPIKE FAILED: DISTINCT ON form was accepted';
    exception when syntax_error_or_access_rule_violation or invalid_column_reference then
        raise notice 'PASS  DISTINCT ON rejected: % (%)', sqlerrm, sqlstate;
    end;
end;
$$;

\echo ''
\echo '--- 4.8  rounding mode, so the .NET side can be configured to agree (S-24) ---'
do $$
begin
    -- PostgreSQL numeric rounds half AWAY FROM ZERO. .NET Math.Round defaults
    -- to banker's rounding (2.5 -> 2), so the Money value object must pass
    -- MidpointRounding.AwayFromZero explicitly. One midpoint test, both sides.
    perform expect('round(2.5)',   round(2.5::numeric)::text,   '3');
    perform expect('round(3.5)',   round(3.5::numeric)::text,   '4');
    perform expect('round(-2.5)',  round(-2.5::numeric)::text,  '-3');
    perform expect('round(0.125,2)', round(0.125::numeric, 2)::text, '0.13');
end;
$$;

\echo ''
\echo '--- 4.9  the window guards (S-17: a trigger, because a CHECK cannot see another table) ---'
do $$
begin
    -- a balance dated after closed_on
    begin
        insert into account_balance
            (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at)
        values ('aaaaaaaa-0000-4000-8000-000000000005', date '2026-07-15', 30.0000, 'USD',
                'manual', 'manual', now());
        raise exception 'SPIKE FAILED: a balance after closed_on was accepted';
    exception when check_violation then
        raise notice 'PASS  balance after closed_on rejected: %', sqlerrm;
    end;

    -- a balance dated before opened_on
    begin
        insert into account_balance
            (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at)
        values ('aaaaaaaa-0000-4000-8000-000000000001', date '2025-12-31', 30.0000, 'USD',
                'manual', 'manual', now());
        raise exception 'SPIKE FAILED: a balance before opened_on was accepted';
    exception when check_violation then
        raise notice 'PASS  balance before opened_on rejected: %', sqlerrm;
    end;

    -- closing an account behind a live balance: the direction S-17 says is
    -- normally unguarded
    begin
        update account set closed_on = date '2026-02-01'
         where id = 'aaaaaaaa-0000-4000-8000-000000000001';
        raise exception 'SPIKE FAILED: an account was closed behind a live balance';
    exception when check_violation then
        raise notice 'PASS  close behind a live balance rejected: %', sqlerrm;
    end;

    -- and the single-currency declaration (M-11)
    begin
        insert into account (institution_id, display_name, currency, opened_on)
        values ('11111111-1111-4111-8111-111111111111', 'Generated Account F', 'EUR', date '2026-01-01');
        raise exception 'SPIKE FAILED: a second currency was accepted';
    exception when check_violation then
        raise notice 'PASS  second currency rejected: %', sqlerrm;
    end;
end;
$$;
