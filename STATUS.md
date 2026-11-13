# Status

**Last updated:** 2026-11-13
**Current milestone:** M1 Core, recurrence, REST API

## In Progress
- M1. Two pull requests are open. PR #2 (branch `m1-entities`)
  holds the entities. The persistence work is on branch
  `m1-persistence`, built on top of `m1-entities`, with its own
  pull request. Merge #2 first; the persistence branch then needs
  main merged into it before it can go in.

## Recently Completed
- Persistence in `Nudge.Web/Data`: `NudgeDbContext`, one mapping
  class per entity, and the first migration, `InitialCreate`. The
  app applies migrations when it starts. Tests in `Nudge.Web.Tests`
  save and reload through separate contexts, and both named
  regression tests are repeated there.
- Entities in `Nudge.Core`: `Item`, `Project` (nested), `Context`,
  `Reminder`, with `ItemState` and `Priority`. Both named
  regression tests exist at item level in `ItemTests`.
- Recurrence engine in `Nudge.Core`: `RecurrenceRule` (RRULE
  subset), `Recurrence` (fixed-schedule and after-completion),
  `LocalTimeResolver` (DST), with unit and CsCheck property tests.
- Deployed to Fly.io as `nudge-gtd` (region `ord`), one machine,
  SQLite on volume `nudge_data`, Litestream replicating to the
  Tigris bucket `nudge-gtd-backup`.

## Next Up
1. Rehearse a restore from the Litestream backup.
2. Change log, soft delete, undo.
3. REST endpoints with OpenAPI; dry-run and idempotency keys.
4. API-token auth.

## Blockers / Open Questions
- None.

## Key Context
- Merging the persistence work is the first deploy that changes
  the production database: at startup the app creates the tables
  in the so far empty `/data/nudge.db`. The Docker image was built
  and run locally with an empty volume; it was not run under
  Litestream.
- To change the schema: change the model, then
  `dotnet tool restore` and
  `dotnet ef migrations add NameOfTheChange --project src/Nudge.Web`,
  and commit the migration with the change. Never edit a migration
  that has been merged. `SchemaTests.Model_MatchesTheMigrations`
  fails if a migration is missing.
- EF Core and `dotnet-ef` are pinned to 10.0.15. EF Core 11 needs
  .NET 11, and Microsoft.Data.Sqlite 11 does not work with EF
  Core 10; do not take the "latest" of either while on net10.0.
- For the REST endpoints (see DECISIONS.md, 2026-11-13):
  - Read all projects before `MoveUnder` or `Ancestors`, and test
    through HTTP that moving a project under its own descendant is
    refused.
  - Read an existing project or context through the request's
    `NudgeDbContext` before giving it to an item.
  - A duplicate context name surfaces as `DbUpdateException`; turn
    it into a clear error.
  - Rows have no version column. EF Core writes only the columns
    that changed, so edits to different fields both survive, and
    two edits to the same field are last-write-wins.
- The first three M1 acceptance criteria hold in the domain and
  through the database, and are tested at both levels. They stay
  unchecked until they are also proven through the API.
- Entities have no soft-delete or timestamp fields yet; those come
  with the change log task. Deleting a project that still has
  items or projects under it is refused by the database.
- No GitHub issues or labels exist yet (`/setup-github` has not
  been run); no pull request so far closed an issue.
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
