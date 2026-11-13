# Status

**Last updated:** 2026-11-13
**Current milestone:** M1 Core, recurrence, REST API

## In Progress
- M1. Two of its six tasks were built on 2026-11-13 and are waiting
  for review. Nothing from that session is on main yet.
  - PR #2, branch `m1-entities`: the entities.
    https://github.com/kpanko/nudge/pull/2
  - PR #3, branch `m1-persistence`: EF Core persistence. It
    contains everything in #2 plus its own commits.
    https://github.com/kpanko/nudge/pull/3
  - CI passes on both. CodeRabbit has not been triggered on either.
- The owner did not follow that session. Everything needed to
  review it is in this file, in the two pull request descriptions,
  and in the eight DECISIONS.md entries dated 2026-11-13.

### How to merge them
Both pull requests target main. Either way ends with everything on
main:

1. One step. Merge #3 with "Create a merge commit". It brings #2
   with it, and GitHub marks #2 as merged. (With "Squash and merge"
   or "Rebase and merge" the result on main is the same, but #2
   stays open and has to be closed by hand.)
2. Two steps, one squashed commit each, as was done for PR #1.
   Squash-merge #2. PR #3 then shows conflicts in STATUS.md,
   DECISIONS.md and `src/Nudge.Core/Project.cs`, because main now
   holds the same changes as a different commit. Bring it up to
   date, then squash-merge it:

   ```bash
   git fetch origin
   git checkout m1-persistence
   git merge -X ours origin/main
   dotnet test
   git push
   ```

   `-X ours` keeps this branch's side wherever the two differ. That
   is right as long as nothing but #2 has gone into main since. It
   was tried on throwaway branches: the result is identical to
   `m1-persistence`.

Merging to main starts a deploy that waits for approval of the
`production` environment. The deploy that includes #3 is the first
one that changes the production database: at startup the app
creates its tables in `/data/nudge.db`, which is empty so far.

## Recently Completed
- Persistence in `Nudge.Web/Data` (PR #3): `NudgeDbContext`, one
  mapping class per entity, and the first migration,
  `InitialCreate`. The app applies migrations when it starts. Tests
  in `Nudge.Web.Tests` save and reload through separate contexts,
  and both named regression tests are repeated there.
- Entities in `Nudge.Core` (PR #2): `Item`, `Project` (nested),
  `Context`, `Reminder`, with `ItemState` and `Priority`. Both
  named regression tests exist at item level in `ItemTests`.
- Recurrence engine in `Nudge.Core`: `RecurrenceRule` (RRULE
  subset), `Recurrence` (fixed-schedule and after-completion),
  `LocalTimeResolver` (DST), with unit and CsCheck property tests.
- Deployed to Fly.io as `nudge-gtd` (region `ord`), one machine,
  SQLite on volume `nudge_data`, Litestream replicating to the
  Tigris bucket `nudge-gtd-backup`.

## Next Up
1. Review and merge PR #2 and PR #3 (see above) and approve the
   deploy.
2. Rehearse a restore from the Litestream backup. It needs the
   owner's Fly and Tigris credentials, and is best done before the
   Todoist import (M4) puts real data in.
3. Change log, soft delete, undo.
4. REST endpoints with OpenAPI; dry-run and idempotency keys.
5. API-token auth.

## Blockers / Open Questions
- No blockers.
- The choices below were made on 2026-11-13 without the owner. Each
  is explained in DECISIONS.md under that date. All are cheap to
  change now and get dearer once the API and the import are built
  on them, so say if one is wrong.
  - In the entities (PR #2):
    1. Lists are `Inbox`, `NextAction`, `WaitingFor`, `Someday`;
       priorities are `None`, `Low`, `Medium`, `High`. Finished is
       not a list; `CompletedAt` records it.
    2. Clearing the due date of a repeating item is refused; it has
       to stop repeating first.
    3. Completing an item that is already finished is an error.
       Retried requests are left to the API's idempotency keys.
    4. A defer date moves with a repeating item, staying the same
       number of days ahead of each occurrence.
    5. A reminder is a fixed instant or an offset from the due
       date. A due date without a time counts as 00:00, so +9 hours
       means 09:00 that day. A relative reminder on an item with no
       due date does not fire.
    6. Setting a list, priority or repeat mode that does not exist
       is an error.
  - In the storage (PR #3):
    7. EF Core 10.0.15 replaces the direct `Microsoft.Data.Sqlite`
       11.0.0 reference.
    8. Migrations run when the app starts, and startup deletes a
       migration lock left by a killed process. Without that, the
       next start waits for ever; this was reproduced.
    9. Context names are unique, ignoring the case of ASCII
       letters. This is a new rule.
    10. A project that still has items or projects under it cannot
        be deleted.
    11. Instants are stored in UTC; the offset they were created
        with is not kept.
    12. Existing files changed, beyond additions: `Project` has a
        private constructor for EF Core; `HealthEndpointTests` uses
        a temporary database of its own; `appsettings.json` turns
        off the logging of every SQL command.
- The open questions in VISION.md are unchanged.

## Key Context
- What was and was not checked on 2026-11-13:
  - `dotnet build` is clean and `dotnet test` passes, in Debug and
    Release: 139 tests on `m1-persistence` (91 core, 48 web), 92 on
    `m1-entities`.
  - The app was started twice against a scratch database, and the
    Docker image was built and run with an empty volume. Both
    times the first start applied the migration, the second found
    nothing to do, and `/healthz` returned 200.
  - Six mappings and the lock fix were each removed in turn to
    confirm that a test fails without them.
  - Not checked: the image running under Litestream, and the
    deploy to Fly.
- To change the schema: change the model, then
  `dotnet tool restore` and
  `dotnet ef migrations add NameOfTheChange --project src/Nudge.Web`,
  and commit the migration with the change. Never edit a migration
  that has been merged. `SchemaTests.Model_MatchesTheMigrations`
  fails if a migration is missing.
- EF Core and `dotnet-ef` are pinned to 10.0.15. EF Core 11 needs
  .NET 11, and Microsoft.Data.Sqlite 11 does not work with EF
  Core 10; do not take the "latest" of either while on net10.0.
- If the app ever hangs at startup after logging "Acquiring an
  exclusive lock for migration application", a migration lock is
  stuck. Startup now removes it, so this should not happen; the
  manual fix is `DROP TABLE "__EFMigrationsLock";`.
- For the REST endpoints (see DECISIONS.md, 2026-11-13):
  - Read all projects before `MoveUnder` or `Ancestors`, and test
    through HTTP that moving a project under its own descendant is
    refused.
  - Read an existing project or context through the request's
    `NudgeDbContext` before giving it to an item.
  - A duplicate context name surfaces as `DbUpdateException`; turn
    it into a clear error.
  - Views of open items must leave out items with `CompletedAt`.
  - `Item.Complete` needs the owner's time zone; where that is
    configured is not decided yet.
  - Rows have no version column. EF Core writes only the columns
    that changed, so edits to different fields both survive, and
    two edits to the same field are last-write-wins.
- For the change log task: there is no way yet to reopen a finished
  item, and entities have no soft-delete or timestamp fields.
- The first three M1 acceptance criteria hold in the domain and
  through the database, and are tested at both levels. They stay
  unchecked until they are also proven through the API.
- No GitHub issues or labels exist yet (`/setup-github` has not
  been run); no pull request so far closed an issue. The repository
  does not delete branches after a merge.
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
