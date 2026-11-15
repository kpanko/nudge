# Status

**Last updated:** 2026-11-14
**Current milestone:** M1 Core, recurrence, REST API

## In Progress
- M1. Five of its six tasks are built and waiting for review; only
  API-token auth is left. Nothing from them is on main yet. Each
  pull request contains the ones before it plus its own commits.
  - PR #2, branch `m1-entities`: the entities.
    https://github.com/kpanko/nudge/pull/2
  - PR #3, branch `m1-persistence`: EF Core persistence.
    https://github.com/kpanko/nudge/pull/3
  - PR #4, branch `m1-change-log`: change log, soft delete, undo.
    https://github.com/kpanko/nudge/pull/4
  - PR #5, branch `m1-rest-api`: REST endpoints with OpenAPI, dry
    runs and idempotency keys.
    https://github.com/kpanko/nudge/pull/5
  - CI passed on #2, #3 and #4. CodeRabbit has not been triggered
    on any of them.
- The owner did not follow the sessions of 2026-11-13 and
  2026-11-14. Everything needed to review them is in this file, in
  the four pull request descriptions, and in the DECISIONS.md
  entries dated 2026-11-13 and 2026-11-14.
- **Before approving the deploy of #5**, set the owner's time zone
  on Fly (see "Must be done by hand" below). Without it the new
  version stops at startup.

### How to merge them
All four pull requests target main. Every route below was tried on
throwaway local branches and ends with main identical to
`m1-rest-api`:

1. One step. Merge #5 with "Create a merge commit". It brings #2,
   #3 and #4 with it, and GitHub marks them as merged. (With
   "Squash and merge" or "Rebase and merge" the result on main is
   the same, but #2, #3 and #4 stay open and have to be closed by
   hand.)
2. One at a time. Merge #2, then #3, then #4, then #5, each with
   "Create a merge commit". Nothing conflicts.
3. One squashed commit each, as was done for PR #1. After a
   squash-merge the next pull request shows conflicts in every file
   both touched, because main then holds the same changes as a
   different commit. Bring the next branch up to date, then
   squash-merge it:

   ```bash
   git fetch origin
   git checkout m1-persistence   # after #2; m1-change-log after
                                 # #3; m1-rest-api after #4
   git merge -X ours origin/main
   dotnet test
   git push
   ```

   `-X ours` keeps the branch's side wherever the two differ. That
   is right as long as nothing but the earlier pull requests has
   gone into main.

Merging to main starts a deploy that waits for approval of the
`production` environment. Startup then applies the three migrations
to the production database at `/data/nudge.db`, which is empty so
far.

### Must be done by hand
- Before approving the first deploy that contains #5, set the
  owner's time zone as a Fly secret, as an IANA id (the zone the
  owner lives in; `Europe/London` is only an example):

  ```bash
  fly secrets set Nudge__TimeZone=Europe/London --app nudge-gtd --stage
  ```

  `--stage` keeps it for the next deploy without restarting the
  running machine. It is not in `fly.toml` because the repository
  is public. Without it the new machine exits at startup with "Set
  Nudge:TimeZone (environment variable Nudge__TimeZone) to the
  owner's IANA time zone ...", which `fly logs` shows, and the
  health check never passes. Locally, `Nudge__TimeZone` does the
  same for `dotnet run` and `docker run -e`; in Development the
  machine's own zone is used when it is unset.
- Until API-token auth is deployed the API is open to anyone who
  can reach the app. Do not put real data in before then.

## Recently Completed
- REST API (PR #5). Every endpoint is under `/api`: items (create
  whole, edit, reschedule, clear the due date, set or stop the
  repeat rule, complete, delete, add or remove a context or a
  reminder), projects, contexts, the change log and undo. Every
  write goes through `WriteRunner`: strict query and body, the
  write lock before reading, `?dryRun=true` (rolled back),
  `Idempotency-Key` (the answer kept in `IdempotencyRecords`, from
  the third migration, `IdempotencyKeys`), and an answer of
  `{dryRun, change, data}`. OpenAPI at `/openapi/v1.json`.
- Change log, soft delete and undo (PR #4). Every save through
  `NudgeDbContext` is recorded as one `Change`, with the values
  before and after of each row, in the same transaction. Items,
  projects and contexts are deleted by marking them, and
  `UndoAsync` takes a change back, all or nothing, as a new change.
  Second migration, `ChangeLogAndSoftDelete`.
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
1. Review and merge PR #2, #3, #4 and #5 (see above), set the
   time zone secret, and approve the deploy.
2. Rehearse a restore from the Litestream backup. It needs the
   owner's Fly and Tigris credentials, and is best done before the
   Todoist import (M4) puts real data in.
3. API-token auth, on its own branch. Attach it to the `/api`
   route group in `ApiRoutes.MapNudgeApi`, and name the token in
   `ChangeSource.For`. Decide whether agent tokens must send an
   `Idempotency-Key`.

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
        be deleted. (PR #4 overtakes this, in 13 and 15: deleting
        marks the project and everything under it, and only
        removing its row is still refused.)
    11. Instants are stored in UTC; the offset they were created
        with is not kept.
    12. Existing files changed, beyond additions: `Project` has a
        private constructor for EF Core; `HealthEndpointTests` uses
        a temporary database of its own; `appsettings.json` turns
        off the logging of every SQL command.
  - In the change log, soft delete and undo (PR #4). The three
    newest DECISIONS.md entries explain these:
    13. Deleting a project deletes everything under it (sub-projects
        at any depth and their items), in one change that one undo
        brings back.
    14. Deleting a context leaves it on its items, hidden; undo puts
        it back. Its name is free meanwhile, and the undo is refused
        if the name has been taken.
    15. The rows of items, projects and contexts cannot be removed
        through the app at all, only marked.
    16. Saving is refused until the context knows who is saving
        (`Source`). What the names will be ("web", a token's name)
        is left to the auth task.
    17. An undo is all or nothing and has no "force": if one value
        it would put back has changed since, it is refused.
    18. Undoing the creation of something marks it deleted rather
        than removing it.
    19. Changes are numbered 1, 2, 3 by the database; every other id
        is a GUID made by the app.
    20. The log is never pruned, and holds the same content as the
        tables.
    21. There is no separate "restore" or "reopen": undoing the
        deletion or the completion does it.
  - In the REST API (PR #5). The DECISIONS.md entries dated
    2026-11-14 explain these:
    22. The owner's time zone is required configuration
        (`Nudge:TimeZone`, an IANA id), checked at startup; in
        Development the machine's zone is used if it is unset. It
        is a Fly secret, not in `fly.toml`, because the repository
        is public.
    23. The API is one route group, `/api`, where auth will attach.
        Every write goes through `WriteRunner`, and a test fails for
        a write endpoint that does not, or that the catalog of
        guarantee tests does not cover. Changes are recorded with
        `Source` "api" until auth names the token.
    24. Every write answers `{dryRun, change, data}`. `change` is
        null when nothing changed. `data` is null after a delete,
        the item after a change to its due date, rule, contexts or
        reminders, and the undone change after an undo. Creates are
        201 without `Location`; dry runs are 200, unless their key
        was already used by the real request, whose kept answer is
        then replayed.
    25. A dry run carries the write out, saves, and rolls back.
    26. Idempotency keys are kept in a table of their own,
        `IdempotencyRecords`, not in the nullable column on
        `Changes` that this file suggested: a column could not hold
        the answer a replay must return, and a write that changes
        nothing has no change to hang a key on. The record is
        written in the same transaction, is not in the change log,
        and is never pruned. Keys are optional; the auth task may
        require them for agent tokens.
    27. A request is identified by method, path and the exact bytes
        of its body; the query (`dryRun`) is left out, and the path
        is compared ignoring case and a trailing slash, as routing
        does (other spellings of an id differ). The same key
        with another request is 422. A failed request keeps
        nothing. A retry after the change was undone is still a
        replay.
    28. Completing a repeating item requires the `occurrence` being
        completed; a stale one is 409. Sending one for an item that
        does not repeat is 409. No ETags.
    29. Known limit of 28: in after-completion mode, completing
        exactly one interval early gives back the same occurrence,
        so a duplicate without a key passes; the result is the same
        as one completion (tested). The key is the full protection.
    30. Requests are strict: on a write, any query parameter but
        `dryRun` (only `true` or `false`), and any unknown,
        repeated, mistyped or wrongly null property, is a 400
        naming it. Property names are case-sensitive; enum names
        are not.
    31. PATCH: left out is kept, null clears. The due date, the
        rule, contexts, reminders and completion are refused there,
        with the endpoint to use instead.
    32. JSON: camelCase; enums as camelCase strings; instants in
        UTC with `Z` (an offset required on input); reminder
        offsets as `offsetMinutes`; the rule as canonical RRULE
        text. Change log entries keep the stored form, so ids are
        upper case there and lower case in resources.
    33. Errors are problem details: 400, 404 (missing or deleted),
        409 (domain refusal, refused undo with `reasons`, context
        name in use, stale occurrence), 413, 415, 422.
    34. `UndoWithinAsync` undoes inside the caller's transaction,
        under a savepoint; `UndoAsync` is unchanged.
    35. The OpenAPI document is served in every environment. XML
        comments are compiled into it, so `Nudge.Web` now generates
        a documentation file, with the warnings for members and
        parameters without comments turned off there.
    36. Nudge.Core signals broken rules with the same exception types
        a bug inside it would throw, so the API cannot tell them
        apart. Each refusal made from an exception is logged at
        Warning with it (never the body). Follow-up, needing the
        owner's agreement because it changes Core: an exception
        type of Core's own for broken rules, the only one the API
        maps.
- The open questions in VISION.md are unchanged.

## Key Context
- What was and was not checked:
  - For PR #5, on 2026-11-14:
    - `dotnet build` is clean (no warnings) and `dotnet test`
      passes, in Debug and Release: 435 tests (95 core, 340 web),
      up from 225. The CI steps (restore, Release build, tests
      without rebuilding) were run locally.
    - Seven safeguards were broken in turn (a dry run committing
      instead of rolling back, the idempotency record not saved,
      the query check, the occurrence check, the body left out of
      the fingerprint, the record logged as a change, the undo
      savepoint not rolled back). A test fails for each.
    - The Docker image was built and run on an empty volume.
      Without a time zone it stopped at startup with the message
      above. With one it applied the three migrations and created
      an item, and a second start found nothing to do.
    - The app was run on a scratch database and driven with curl:
      a dry-run create left nothing; a keyed create and its retry
      gave the same 201 body, the retry with
      `Idempotent-Replayed: true`; `?dry_run=true` was a 400;
      completing advanced the item once, and the same request
      again was a 409; undo put the occurrence back;
      `/openapi/v1.json` returned the document.
    - Not checked: the deploy to Fly, the image under Litestream,
      and the API from the planning agent or any real client.
  - For PRs #2 and #3, on 2026-11-13:
    - `dotnet build` is clean and `dotnet test` passes, in Debug
      and Release: 139 tests on `m1-persistence` (91 core, 48 web),
      92 on `m1-entities`.
    - The app was started twice against a scratch database, and the
      Docker image was built and run with an empty volume. Both
      times the first start applied the migration, the second found
      nothing to do, and `/healthz` returned 200.
    - Six mappings and the lock fix were each removed in turn to
      confirm that a test fails without them.
    - Not checked: the image running under Litestream, and the
      deploy to Fly.
  - For PR #4:
    - `dotnet build` is clean and `dotnet test` passes, in Debug
      and Release: 225 tests (95 core, 130 web).
    - 40 safeguards were each broken in turn (a guard removed, a
      filter dropped, a check inverted) to confirm that a test
      fails. All are caught.
    - The app was started on a fresh scratch database, and on one
      left at the first migration with rows in it. The second
      migration applied, the rows were intact, a second start found
      nothing to do, and `/healthz` returned 200.
    - The Docker image was built from the committed code and run
      twice on an empty volume: the first start applied both
      migrations, the second found nothing to do, and `/healthz`
      returned 200 both times.
    - Not checked: two writers at once, the image under Litestream,
      and the deploy to Fly.
- Known limits of the change log:
  - Only saves through the context are logged. `ExecuteUpdate`,
    `ExecuteDelete` or SQL on these tables would go unrecorded.
  - The "before" values are what the context read. Two requests
    that read and write the same row at the same moment can log a
    "before" that was already out of date.
  - `Project.Delete` on its own marks only the project; nothing
    checks at save time that its contents went too.
  - An entry made before a column is renamed or dropped, or before
    the way a value is stored changes, cannot be undone. It is
    refused with a message.
- To change the schema: change the model, then
  `dotnet tool restore` and
  `dotnet ef migrations add NameOfTheChange --project src/Nudge.Web`,
  and commit the migration with the change. Never edit a migration
  that has been merged. `SchemaTests.Model_MatchesTheMigrations`
  fails if a migration is missing. A migration that renames or
  drops a column, or changes how a value is stored, makes older
  change log entries impossible to undo (they are refused); rewrite
  them in the migration if that matters.
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
  - Set `db.Source` (and `Description`) before saving, in one place
    for all write endpoints; a save without it throws. (Done in
    `WriteRunner`, with the name from `ChangeSource`.)
  - Begin a transaction before reading in each write request
    (`db.Database.BeginTransactionAsync()` takes SQLite's write
    lock at once), so the log's "before" values are true and writes
    do not overwrite each other. Not around an undo, which starts
    its own.
  - `db.UndoAsync(number)`: null means no such change (404);
    `UndoRefusedException` carries `Reasons` (409). It needs a
    context with nothing unsaved and no open transaction, and the
    context forgets everything it had read.
  - Delete a project with `db.DeleteProjectAsync(id, now)` and then
    save; false means not found. Never `project.Delete` on its own.
  - Deleted things are not returned by normal queries, so a request
    for one is "not found" for free; a view of deleted things needs
    `IgnoreQueryFilters()`. Restoring one is undoing the change that
    deleted it, found through `ChangeEntries` by `TableName` and
    `RowKey`.
  - Dry-run: `ChangeRecorder.Capture(db)` returns the entries a save
    would log, without saving.
  - Idempotency keys: a nullable unique `IdempotencyKey` column on
    `Changes`, written with the change, is the suggested shape.
    (Not followed: they have a table of their own; see 26 above.)
  - Reach reminders through `db.Items`, never on their own, or the
    reminders of deleted items turn up (matters for M5).
  - `Integrity.ProblemsAsync` lists broken whole-data rules; a
    health page could show it (M5).
- The four M1 acceptance criteria about recurrence, rescheduling
  and the change log are now proven through HTTP as well, and are
  checked in MILESTONES.md: `AcceptanceTests` in
  `tests/Nudge.Web.Tests/Api` for the first three, and
  `WriteGuaranteeTests.Write_IsLoggedAndCanBeUndone`, run on every
  write endpoint, for the fifth.
- To add a write endpoint: map it with `MapWrite` and add a request
  for it to `WriteCatalog`; the guard test fails until both are
  done.
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
