# Status

**Last updated:** 2026-11-13
**Current milestone:** M1 Core, recurrence, REST API

## In Progress
- M1. Recurrence engine (PR #1) is merged; entities are in review
  on branch `m1-entities`. Persistence is next.

## Recently Completed
- Entities in `Nudge.Core`: `Item`, `Project` (nested), `Context`,
  `Reminder`, with `ItemState` and `Priority`. Both named
  regression tests now exist at item level in `ItemTests`.
- Recurrence engine in `Nudge.Core`: `RecurrenceRule` (RRULE
  subset), `Recurrence` (fixed-schedule and after-completion),
  `LocalTimeResolver` (DST), with unit and CsCheck property tests.
- Deployed to Fly.io as `nudge-gtd` (region `ord`), one machine,
  SQLite on volume `nudge_data`, Litestream replicating to the
  Tigris bucket `nudge-gtd-backup`.

## Next Up
1. Rehearse a restore from the Litestream backup.
2. M1 persistence: EF Core + SQLite mappings and migrations for the
   entities. They have no parameterless constructors and expose
   read-only collections, so the mappings need constructor binding
   or private constructors, and backing-field access.
3. Change log, soft delete, undo.

## Blockers / Open Questions
- None.

## Key Context
- The first three M1 acceptance criteria hold in the domain and are
  tested there. They stay unchecked until they are also proven
  through persistence and the API.
- Entities have no soft-delete or timestamp fields yet; those come
  with the change log task.
- No GitHub issues or labels exist yet (`/setup-github` has not
  been run); PR #1 closed no issue.
- CodeRabbit reviews pull requests when triggered. Its findings on
  PR #1 were valid and are fixed.
- `RecurrenceRule` returns null when nothing matches within 400
  years; this is deliberate and means "stops repeating".
- MAUI and Android workloads are installed for .NET 9; the .NET 10
  workloads have not been checked.
- `dotnet workload list` hangs on this machine; avoid it.
- Pushes to main deploy after approval of the `production`
  environment in GitHub Actions. The local
  flyctl is old and failed to allocate IPs on first deploy; they
  were allocated by hand.
- A restore from backup has not been rehearsed yet.
