-- Spike D, part 2: the fixture.
--
-- Constitution IX: invented institutions, invented amounts, invented dates.
-- Nothing here is anybody's money. The shape is chosen to make three specific
-- defects visible rather than to look realistic:
--
--   * two accounts observed on a date and two in-window but never observed,
--     which is the case CROSS JOIN silently cancels;
--   * a stretch where every in-window account is unverified, which is the case
--     sum() returns NULL for;
--   * an account that closes, which is the case Principle III forbids filling
--     past.

\set ON_ERROR_STOP on
set search_path to spike, public;

-- Amendment 3 condition 2: the cutoff is NAMED, here, once.
-- 45 days = a monthly hand-entry cadence plus slack. See RESULTS.md: slice-01
-- step 2 does not name a value, and this one is the spike's proposal, not a
-- ruling.
insert into app_setting (balance_staleness_days) values (45);

insert into institution (id, name) values
    ('11111111-1111-4111-8111-111111111111', 'Generated Institution Alpha'),
    ('22222222-2222-4222-8222-222222222222', 'Generated Institution Beta');

insert into account (id, institution_id, display_name, currency, opened_on, closed_on) values
    ('aaaaaaaa-0000-4000-8000-000000000001', '11111111-1111-4111-8111-111111111111',
     'Generated Account A', 'USD', date '2026-01-01', null),
    ('aaaaaaaa-0000-4000-8000-000000000002', '11111111-1111-4111-8111-111111111111',
     'Generated Account B', 'USD', date '2026-01-01', null),
    ('aaaaaaaa-0000-4000-8000-000000000003', '22222222-2222-4222-8222-222222222222',
     'Generated Account C', 'USD', date '2026-01-01', null),
    ('aaaaaaaa-0000-4000-8000-000000000004', '22222222-2222-4222-8222-222222222222',
     'Generated Account D', 'USD', date '2026-01-01', null),
    -- opens after the measurement date, so it is not in the four-account
    -- reading, and closes, so Principle III has something to stop.
    ('aaaaaaaa-0000-4000-8000-000000000005', '22222222-2222-4222-8222-222222222222',
     'Generated Account E', 'USD', date '2026-06-01', null);

insert into account_source (account_id, source_system, source_id) values
    ('aaaaaaaa-0000-4000-8000-000000000001', 'manual', 'gen-a'),
    ('aaaaaaaa-0000-4000-8000-000000000002', 'manual', 'gen-b'),
    ('aaaaaaaa-0000-4000-8000-000000000003', 'manual', 'gen-c'),
    ('aaaaaaaa-0000-4000-8000-000000000004', 'manual', 'gen-d'),
    ('aaaaaaaa-0000-4000-8000-000000000005', 'manual', 'gen-e');

insert into account_balance
    (id, account_id, as_of_date, balance, currency, source_system, source_strength, observed_at) values
    -- A and B observed on the measurement date.
    ('bbbbbbbb-0000-4000-8000-000000000001', 'aaaaaaaa-0000-4000-8000-000000000001',
     date '2026-03-10', 100.0000, 'USD', 'manual', 'manual', timestamptz '2026-03-10 09:00+00'),
    ('bbbbbbbb-0000-4000-8000-000000000002', 'aaaaaaaa-0000-4000-8000-000000000002',
     date '2026-03-10',  50.0000, 'USD', 'manual', 'manual', timestamptz '2026-03-10 09:00+00'),
    -- C is in-window on the measurement date but its first observation is
    -- later: in-window, unverified, contributes NULL.
    ('bbbbbbbb-0000-4000-8000-000000000003', 'aaaaaaaa-0000-4000-8000-000000000003',
     date '2026-04-20', 200.0000, 'USD', 'manual', 'manual', timestamptz '2026-04-20 09:00+00'),
    -- D is never observed at all.
    -- E opens, is observed once, and is closed below.
    ('bbbbbbbb-0000-4000-8000-000000000005', 'aaaaaaaa-0000-4000-8000-000000000005',
     date '2026-06-05',  25.0000, 'USD', 'manual', 'manual', timestamptz '2026-06-05 09:00+00');

-- Closing goes through the guard, which is the direction S-17 says is normally
-- unprotected. The live balance (06-05) precedes the close (06-30), so it passes.
update account set closed_on = date '2026-06-30'
 where id = 'aaaaaaaa-0000-4000-8000-000000000005';
