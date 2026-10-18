-- Spike G, part 1: the fixture. Constitution IX: invented institution, account and amounts.
--
-- One account, open from 2026-01-01. Nothing else: the question is which of an account's
-- OWN rows the view counts, so a second account would only add noise to the sum.

\set ON_ERROR_STOP on
set search_path to public;

insert into institution (id, name) values
    ('33333333-3333-4333-8333-333333333333', 'Generated Institution Gamma');

insert into account (id, institution_id, display_name, currency, opened_on) values
    ('aaaaaaaa-0000-4000-8000-0000000000a1', '33333333-3333-4333-8333-333333333333',
     'Generated Account G', 'USD', date '2026-01-01');
