-- Spike D, part 1: the schema slice-01 step 2 describes.
--
-- Scope: exactly the tables the net-worth chart needs, with the provenance
-- columns Constitution I and IV require. Not a migration -- a spike. The EF
-- Core migrations in step 2 are written against what this file proves, not
-- generated from it.

\set ON_ERROR_STOP on

drop schema if exists spike cascade;
create schema spike;
set search_path to spike, public;

-- Constitution IV: sources are not equally strong, and the ordering is part of
-- the type, not a convention. Strongest first.
create type source_strength as enum ('statement', 'export', 'api', 'scrape', 'manual');

-- M-11: the slice is single-currency. R2-M9 forbids expressing that as a
-- cross-row CHECK (a CHECK cannot see another row); a literal is legal and is
-- what "declare the slice single-currency" means.
create domain slice_currency as char(3)
  constraint slice_is_single_currency check (value = 'USD');

create table institution (
    id          uuid primary key default gen_random_uuid(),
    name        text not null,
    created_at  timestamptz not null default now(),
    deleted_at  timestamptz
);

-- M-10: `account` is an INTERNAL entity. It carries no external identity, so
-- nothing about it has to change when a second source starts reporting the
-- same real-world account. Identity lives one table over.
create table account (
    id              uuid primary key default gen_random_uuid(),
    institution_id  uuid not null references institution (id),
    display_name    text not null,
    currency        slice_currency not null,
    opened_on       date not null,
    closed_on       date,
    created_at      timestamptz not null default now(),
    deleted_at      timestamptz,
    constraint account_window_ordered
        check (closed_on is null or closed_on >= opened_on)
);

-- M-10, the fix taken now because retrofitting identity is the expensive path.
-- FULL unique index, not partial: a source id that has been seen must never be
-- re-bindable to a different account by soft-deleting the first binding.
create table account_source (
    id             uuid primary key default gen_random_uuid(),
    account_id     uuid not null references account (id),
    source_system  text not null,
    source_id      text not null,
    created_at     timestamptz not null default now(),
    constraint account_source_identity unique (source_system, source_id)
);

-- Constitution I: every externally-sourced row carries its origin in the
-- schema. Constitution IV: and the strength of that origin.
create table account_balance (
    id              uuid primary key default gen_random_uuid(),
    account_id      uuid not null references account (id),
    as_of_date      date not null,
    balance         numeric(19,4) not null,
    currency        slice_currency not null,
    source_system   text not null,
    source_id       text,
    source_strength source_strength not null,
    observed_at     timestamptz not null,
    -- M-9 / R2-B3: the pointer is BACKWARD. `supersedes` names a row that
    -- already exists; a forward `superseded_by` would have to name one that
    -- does not yet, and aborts on the foreign key. `superseded_at` is the
    -- discriminator the partial index reads.
    supersedes      uuid references account_balance (id),
    superseded_at   timestamptz,
    deleted_at      timestamptz,
    created_at      timestamptz not null default now()
);

-- M-9: one live row per (account, date, source). Partial, because superseded
-- and soft-deleted rows must remain queryable -- the restatement history is
-- the point.
create unique index account_balance_live_uq
    on account_balance (account_id, as_of_date, source_system)
    where deleted_at is null and superseded_at is null;

-- A row may be superseded by at most one successor, or the history is a graph
-- and "the current value" stops being well defined.
create unique index account_balance_supersedes_uq
    on account_balance (supersedes)
    where supersedes is not null;

create index account_balance_series
    on account_balance (account_id, as_of_date desc)
    where deleted_at is null and superseded_at is null;

-- S-17: the closed_on guard CANNOT be a CHECK constraint -- it references
-- another table. A trigger, in BOTH directions, because the direction that
-- actually happens is closing an account after the balances exist, and that
-- one is unguarded if you only write the first half.
create function account_balance_window_guard() returns trigger
language plpgsql as $$
declare
    a_opened date;
    a_closed date;
begin
    select opened_on, closed_on into a_opened, a_closed
      from account where id = new.account_id;

    if new.as_of_date < a_opened then
        raise exception 'balance % precedes account opened_on %', new.as_of_date, a_opened
            using errcode = 'check_violation';
    end if;

    if a_closed is not null and new.as_of_date > a_closed then
        raise exception 'balance % follows account closed_on % (Constitution III)',
            new.as_of_date, a_closed
            using errcode = 'check_violation';
    end if;

    return new;
end;
$$;

create trigger account_balance_window_guard
    before insert or update of as_of_date, account_id on account_balance
    for each row execute function account_balance_window_guard();

create function account_close_guard() returns trigger
language plpgsql as $$
declare
    latest date;
begin
    if new.closed_on is null then
        return new;
    end if;

    select max(as_of_date) into latest
      from account_balance
     where account_id = new.id
       and deleted_at is null
       and superseded_at is null;

    if latest is not null and latest > new.closed_on then
        raise exception 'account has a live balance at % after the proposed closed_on %',
            latest, new.closed_on
            using errcode = 'check_violation';
    end if;

    return new;
end;
$$;

create trigger account_close_guard
    before update of closed_on on account
    for each row execute function account_close_guard();

-- Amendment 3, condition 2: "active window" is not "up to today". The carry
-- expires, and the number of days it survives is part of the amendment, not an
-- implementation detail -- so it is a stored setting with one row, not a
-- literal buried in a view body.
create table app_setting (
    id                      boolean primary key default true,
    balance_staleness_days  integer not null,
    constraint app_setting_single_row check (id),
    constraint app_setting_staleness_positive check (balance_staleness_days > 0)
);
