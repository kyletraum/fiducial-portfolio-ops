# Build log

One line per work session. **Write it as you go, not at the gate.**

This exists because `D-030` §2's stop gate asks *"what did it actually cost?"*,
and a figure reconstructed months later is a recalled figure — which the planning
system that commissioned this project forbids outright as a source. Hours are the
one input the assessment needs that **cannot be recovered afterwards**. Everything
else (what shipped, what broke, what is green) is in git.

Keep it boring. Date, step, hours, one clause. The value is the column of numbers
at the bottom, not the prose.

**Two hour columns, and only one of them gates.** `Kyle h` is evenings — the unit
the 200-hour abandon threshold counts. `Agent h` is wall-clock of an agent
session. **Agent hours do NOT count toward the threshold** (Kyle, 2026-09-20).
They are still recorded, because a cost nobody wrote down cannot be argued about
later; they are simply not the number the gate reads.

| Date | Step | Kyle h | Agent h | What actually happened |
|---|---|---|---|---|
| 2026-09-18 | 0 · repo | 0.5 | — | Repo created, Constitution IX controls landed, specs ported |
| 2026-09-18 | D · spike | — | 0.2 | Spike D: schema, supersession order, `v_net_worth_daily`; all assertions green |
| 2026-09-20 | D · spike | — | 0.3 | Staleness-cutoff sensitivity sweep; 45d proposed with a derivation rather than a guess |
| 2026-09-20 | 2 · data | — | 0.3 | Cadence + batch-entry modelling moved the cutoff 45d -> 90d; `D-034` ruled it; `slice-01.md` step 2 now names it |
| 2026-09-20 | 0 · gate | — | 0.1 | Kyle ruled agent hours out of the threshold; `LOG.md` split into two columns and the threshold reworded |
| 2026-09-24 | 0 · tooling | — | 0.2 | spec-kit 0.14.3.dev0 installed; constitution moved to `.specify/memory/`; stranded ruling commit merged as #4 |
| 2026-09-24 | A · spike | — | 0.5 | Spike A on Aspire 13.5.4: same directory stops the first stack even with `--isolated`; two directories work; no `--launch-profile`, selection is configuration |
| 2026-09-24 | C · spike | — | 0.4 | Spike C: one-shot Migrator + `WaitForCompletion` holds the API, fails it on exit 1, publishes as `service_completed_successfully`; Migrator's wait on pg degrades to `service_started` |

<!-- Add a row per session. Total at the gate; do not total as you go. -->

<!--
UNITS NOTE -- RAISED 2026-09-18, RULED 2026-09-20. The 200-hour abandon
threshold assumes Kyle-evenings. Agent rows are wall-clock of an agent session
and are NOT the same unit; summing one column across both would have given the
gate a number that means nothing. KYLE'S RULING: agent hours do not count
toward the threshold. Implemented as two columns rather than by dropping the
agent figure, so the cost still exists to be argued about even though it does
not gate.

WHAT THE RULING DOES, STATED PLAINLY BECAUSE IT IS NOT OBVIOUS: the 200-hour
threshold no longer bounds what the PROJECT costs. It bounds what KYLE spends.
If most of the work is done by agent sessions, the threshold cannot trip no
matter how long the slice runs, and the stop gate then rests entirely on its
other limb -- three named techniques with the commit that demonstrates each.
That is a coherent position (the threshold was always about whether Kyle's
evenings were well spent) and it is Kyle's call; it is written down here so the
assessment does not later mistake an untripped threshold for evidence of a
cheap project.
-->
