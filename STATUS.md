# Status

**Last updated:** 2026-11-15
**Current milestone:** M1 Core, recurrence, REST API

## In Progress
- M1. All six tasks are built and waiting for review. Nothing from
  them is on main yet, and nothing has been deployed, so M1 stays
  `in-progress`: by MILESTONES.md a milestone ends deployed and
  tested. What is left: review and merge the five pull requests
  below, set the time zone secret, approve the deploy, make the
  first API token on the server, and call the deployed API with it
  (see "Must be done by hand"). Each pull request contains the ones
  before it plus its own commits.
  - PR #2, branch `m1-entities`: the entities.
    https://github.com/kpanko/nudge/pull/2
  - PR #3, branch `m1-persistence`: EF Core persistence.
    https://github.com/kpanko/nudge/pull/3
  - PR #4, branch `m1-change-log`: change log, soft delete, undo.
    https://github.com/kpanko/nudge/pull/4
  - PR #5, branch `m1-rest-api`: REST endpoints with OpenAPI, dry
    runs and idempotency keys.
    https://github.com/kpanko/nudge/pull/5
  - PR #6, branch `m1-api-auth`: API-token auth.
    https://github.com/kpanko/nudge/pull/6
  - CI passed on all five (#5 at f495212, #6 at 3c15b05).
    CodeRabbit has not been triggered on any of them.
- The owner did not follow the sessions of 2026-11-13 and
  2026-11-14. Everything needed to review them is in this file, in
  the five pull request descriptions, and in the DECISIONS.md
  entries dated 2026-11-13 and 2026-11-14.
- **Before approving the deploy of #5**, set the owner's time zone
  on Fly (see "Must be done by hand" below). Without it the new
  version stops at startup.
- M2, the alarm spike, task 1 (built, waiting for the owner's phone):
  PR #7, branch `m2-alarm-spike`, `spikes/AlarmSpike`.
  https://github.com/kpanko/nudge/pull/7
  It is cut from main and **independent of #2 to #6**: it can be
  merged before or after them, with "Create a merge commit" or a
  squash. It touches only its own folder and the M2 section of
  MILESTONES.md (M2 `in-progress`, task 1 checked); a test merge
  into `m1-api-auth` had no conflict. The app is not in `Nudge.sln`
  and not in the Docker image, so merging deploys nothing new, though
  the push to main still starts the deploy job, which waits for
  approval. The phone checklist and results table are in
  `spikes/AlarmSpike/README.md`. CI was not watched.

### How to merge them
All five pull requests target main. Each route below was played
from start to end in a throwaway clone, twice: once as the branches
are, and once with a review fix pushed to `m1-entities` after the
later branches were cut. As they are, every route ends with main
identical to `m1-api-auth`.

STATUS.md on the older branches, and the descriptions of #3 to #5
before 2026-11-14, gave `git merge -X ours` for the squash route.
That is unsafe; do not follow it. These instructions replace it.

1. One step. Merge #6 with "Create a merge commit". It brings #2,
   #3, #4 and #5 with it, and GitHub marks them as merged. (With
   "Squash and merge" or "Rebase and merge" the result on main is
   the same, but #2 to #5 stay open and have to be closed by hand.)
   To deploy the API without auth for a while, merge #5 this way
   instead, and #6 later; but see "Must be done by hand".
   If one of #2 to #5 is still open after #6 is merged, it holds
   commits that #6 lacks (a fix pushed to it later), and it must be
   merged as well.
2. One at a time. Merge #2, then #3, #4, #5 and #6, each with
   "Create a merge commit". Nothing conflicts, and a fix pushed to
   an earlier branch is carried along by merging that branch.
3. One squashed commit each, as was done for PR #1. After a
   squash-merge the next pull request shows conflicts in every file
   both touched, because main then holds the same changes as a
   different commit. After each squash-merge, set the branch just
   merged and the next one, run both checks, and only then bring
   the next branch up to date and squash-merge it:

   ```bash
   PREV=m1-entities NEXT=m1-persistence   # after #2; then
   # m1-persistence m1-change-log after #3, m1-change-log
   # m1-rest-api after #4, m1-rest-api m1-api-auth after #5
   git fetch origin
   # 1. Main must hold exactly the branch just merged.
   git diff origin/main origin/$PREV
   # 2. The next branch must already hold everything in it.
   git diff origin/$PREV $(git merge-base origin/$PREV origin/$NEXT)
   # Both must print nothing. Then:
   git checkout $NEXT
   git pull --ff-only
   git merge -s ours origin/main
   dotnet test
   git push
   ```

   `-s ours` records the merge and keeps the next branch's files as
   they are, which is right only when both checks print nothing.
   If check 2 prints something, a commit reached `$PREV` after the
   next branch was cut: main has it, and `-s ours` would drop it
   from the next branch, so the next squash-merge would take it off
   main again. Instead, merge it up the stack with ordinary merges,
   from `$NEXT` to `m1-api-auth`, testing and pushing each:

   ```bash
   git checkout m1-persistence && git pull --ff-only
   git merge origin/m1-entities && dotnet test && git push
   git checkout m1-change-log && git pull --ff-only
   git merge origin/m1-persistence && dotnet test && git push
   # ... and so on up to m1-api-auth
   git fetch origin
   ```

   (start from `$NEXT` and its `$PREV`), then run the checks again
   and carry on. Played with such a fix on `m1-entities`, check 2
   stopped the first round; after the fix was merged up the stack
   the route ended with the fix on main and everything else
   identical to `m1-api-auth`, and `dotnet test` passed there. In
   later rounds `$PREV` ends in the `-s ours` merge, whose files are
   those of its first parent, so check 2 holds there too (it passed
   in every round of both plays).

   `-X ours`, given before, is unsafe: tried on #6 it kept main's
   side of hunks that did not conflict, and left `Program.cs`
   mapping the API and the OpenAPI document twice, the second time
   without auth.

Merging to main starts a deploy that waits for approval of the
`production` environment. Startup then applies the four migrations
(the fourth, `ApiTokens`, comes with #6) to the production database
at `/data/nudge.db`, which is empty so far.

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
- After the first deploy that contains #6, make a token on the
  server. It is printed once, on stdout; store it where the client
  will read it (a password manager, the MCP server's secret):

  ```bash
  fly ssh console --app nudge-gtd -C "dotnet /app/Nudge.Web.dll token create planning-agent"
  ```

  Run it only after the new version has finished starting (the
  health check passes): on a database with a migration still
  pending the command refuses and changes nothing. The first line
  on stderr reads `Database: /data/nudge.db`. If it names another
  file and says there is no database there, the session lacks the
  image's `ConnectionStrings__Nudge`; nothing was created. Open a
  shell with `fly ssh console --app nudge-gtd` and run it there as

  ```sh
  ConnectionStrings__Nudge='Data Source=/data/nudge.db' dotnet /app/Nudge.Web.dll token create planning-agent
  ```

  Then check it with `curl -H "Authorization: Bearer <token>"
  https://nudge-gtd.fly.dev/api/items` (200; without the header,
  401). `token list` shows the tokens; `token revoke <name-or-id>`
  stops one at once. Names are never reused, revoked ones included.
- If only #5 is deployed, before #6, the API is open to anyone who
  can reach the app. Do not put real data in before #6 is deployed.
- The alarm spike (M2 task 2), on the phone:
  1. Install the APK. It is already built on the dev machine at
     `C:\Users\panko\src\nudge\spikes\AlarmSpike\bin\Release\net9.0-android\nudge.spike.alarms-Signed.apk`
     (from the code in PR #7; to rebuild it, see the spike's README).
     With USB debugging: `adb install -r <that path>`; or copy it to
     the phone and open it in Files, allowing "Install unknown apps".
  2. Open it and allow notifications. Do not exempt it from battery
     optimisation.
  3. Run the checklist in `spikes/AlarmSpike/README.md` (steps 0 to
     9: baselines with the app open and swiped from recents, Doze,
     airplane mode, two reboot cases, the three actions, swipe-away),
     adding a note in the app before each step.
  4. Copy or pull the log, fill in the README's results table, and
     decide MAUI or Kotlin. Record the decision in DECISIONS.md and
     tick the M2 criteria and task 2 in MILESTONES.md. Keep the log
     out of the repository if your notes in it are personal.

## Recently Completed
- API-token auth (PR #6). Tokens are `nudge_` and 32 random bytes;
  only their SHA-256 hash is stored, in `ApiTokens` (fourth
  migration), outside the change log and undo. They are made,
  listed and revoked with `dotnet Nudge.Web.dll token create|list|
  revoke`, never over HTTP. Every endpoint needs
  `Authorization: Bearer <token>` through the fallback policy,
  except `/healthz` and `/openapi/v1.json`; every failure is the
  same 401. Changes are recorded as made by `api:<token name>`.
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
1. Review and merge PR #2, #3, #4, #5 and #6 (see above), set the
   time zone secret, and approve the deploy.
2. Rehearse a restore from the Litestream backup. It needs the
   owner's Fly and Tigris credentials, and is best done before the
   Todoist import (M4) puts real data in.
3. After the deploy, make the first token and call the API with it
   (see "Must be done by hand"); then M1 can be marked `done`.
4. M2: run the alarm spike's checklist on the phone (PR #7; see
   "Must be done by hand"). It does not wait for M1.

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
  - In API-token auth (PR #6). The three newest DECISIONS.md
    entries explain these:
    37. A token is `nudge_` and 32 random bytes in base64url; only
        its SHA-256 hash (hex) is stored, in `ApiTokens`. No
        expiry, no scopes, no last-used column, no rate limiting;
        all deferred.
    38. Tokens are managed only by the command line on the same
        executable (`token create <name>`, `token list`,
        `token revoke <name-or-id>`), not over HTTP, so a token
        cannot mint another. The web UI may add it behind sign-in
        (M3). The command prints the database it uses on stderr.
    39. Names: 1 to 64 letters, digits, `-`, `_`, `.`; unique
        among all tokens, revoked included, ignoring the case of
        ASCII letters; and not shaped like a GUID, since `revoke`
        takes a name or an id. Case-insensitivity and the GUID rule
        are additions to the design given for this task.
    40. The command line applies migrations only when some are
        pending, as startup does, so on the running server it leaves
        the migration lock alone. It does not need `Nudge:TimeZone`.
    41. Every endpoint needs a token through the authorization
        fallback policy; only `/healthz` and `/openapi/v1.json` are
        anonymous. A path that matches no endpoint is also 401
        without a token, not 404.
    42. Every failure is the same 401 with `WWW-Authenticate:
        Bearer` and the same problem details. Only the
        `Authorization` header is read; the scheme name `Bearer` is
        accepted in any case.
    43. Making or revoking a token records no change; token rows are
        out of the change log and out of undo. Each change made
        through the API has the source `api:<token name>`.
    44. Idempotency keys stay optional and are not scoped per token.
        The MCP server (M6) must always send one.
    45. Startup's database preparation moved from `Program.cs` into
        `DatabaseSetup.Prepare`, unchanged. (The command line no
        longer uses it; see 48.)
    46. Existing tests changed: the `Api` helper sends a token by
        default (`SendAsAsync` sends another or none);
        `ProjectContextChangeTests` expects `api:tests` as the
        source; `TestDatabase.DataTables` and `Api.Dump` leave out
        `ApiTokens`, as they leave out `IdempotencyRecords`;
        `OpenApiTests` also checks the 401 and the requirement.
    47. The merge route with squashed commits now uses two checks
        and `git merge -s ours` instead of `-X ours`; see "How to
        merge them". The descriptions of #3, #4 and #5 point there.
  - After the security review of PR #6, on 2026-11-15:
    48. The token command never creates or migrates a database; this
        replaces 40. It opens the database with SQLite's
        `Mode=ReadWrite`, refuses (exit 1, nothing created) when the
        file is missing, naming the path and
        `ConnectionStrings__Nudge`, and refuses a database with a
        pending migration, saying to start the app first. Only the
        app migrates, at startup.
    49. The request log (`Microsoft.AspNetCore.Hosting.Diagnostics`,
        which writes each URL with its query) is held at Warning in
        code, so a token sent in the query string cannot reach the
        log whatever the settings file says.
  - In the alarm spike (PR #7), on 2026-11-15. The DECISIONS.md
    entries of that date about the spike explain these:
    50. The spike is its own project in `spikes/AlarmSpike`, not in
        `Nudge.sln`, on a branch cut from main, so it can be merged
        on its own and the server's build and CI never see it. It is
        a real MAUI app (`MauiApplication`), Android only.
    51. It targets `net9.0-android` with MAUI 9.0.51, and a
        `global.json` in its folder selects the .NET 9 SDK: the
        .NET 10 SDK here has the MAUI and Android workload manifests
        but not their packs, and installing was not allowed. Moving
        to .NET 10 is described in its README. .NET 9 is at or near
        the end of its support; that is acceptable for a throwaway
        spike, not for M5.
    52. Android 8.0 (API 26) and later, target API 35. Release builds
        a plain APK (not the Play bundle) for arm64 and x64, signed
        with the local debug key.
    53. Exact alarms: `USE_EXACT_ALARM`, which Android 13+ grants at
        install and the user cannot revoke (Play allows it only for
        alarm and calendar apps, irrelevant when sideloading), plus
        `SCHEDULE_EXACT_ALARM` capped at API 32 for Android 12 and
        12L. Also `POST_NOTIFICATIONS` (asked at first start on 13+)
        and `RECEIVE_BOOT_COMPLETED`. No battery-optimisation
        request, and no `INTERNET`.
    54. Pending alarms are a tab-separated file in private storage,
        written atomically; the event log is an append-only text
        file in the app's folder on shared storage, so `adb pull`
        reads it from a Release build, and also goes to logcat (tag
        `AlarmSpike`). No database.
    55. Notifications: one high-importance channel, category
        reminder, not ongoing. Complete removes the alarm; Snooze
        brings it back in 2 minutes; Schedule opens the app (an
        activity intent, as Android 12+ requires) and removes the
        alarm, since the spike has no time picker; tapping the body
        only opens the app.
    56. A swipe (delete intent, "Clear all" included) brings the
        alarm back after 30 s. That is a test value; the re-notify
        interval stays an open question in VISION.md.
    57. The boot receiver handles `BOOT_COMPLETED`,
        `MY_PACKAGE_REPLACED` and `QUICKBOOT_POWERON`: it registers
        pending alarms again, fires overdue ones at once (logged
        LATE), and re-posts unanswered notifications. The app is not
        direct-boot aware, so this happens at the first unlock.
        Opening the app does not re-register anything; a button
        does, so the log shows what the boot receiver did alone.
    58. On time means a delay under 10 s against the intended time;
        the log marks anything later LATE. Each process start is
        logged with how long MAUI's start-up took, to separate
        Android's delay from MAUI's.
    59. Android does not enter deep Doze while an alarm clock is due
        within an hour (`min_time_to_alarm`), and leaves Doze to
        deliver one. So the app has a 3-hour button for a real Doze
        test, and the adb Doze test shortens that setting for its
        duration. The log records the Doze, screen and battery saver
        state at each delivery.
    60. Warnings are errors in the spike too (the repository's
        `Directory.Build.props` applies); the build has none.
- The open questions in VISION.md are unchanged.

## Key Context
- What was and was not checked:
  - For the alarm spike (PR #7), on 2026-11-15:
    - The Release build from the command line is clean (no
      warnings). The APK path is under "Must be done by hand".
    - On the Pixel 7 API 35 emulator already installed (run with
      `-read-only`, so the AVD is unchanged), with the Release APK
      and the process killed before each alarm: fired with the
      screen off in airplane mode (+3.6 s) and in forced deep Doze
      (+3.8 s); the delays were mostly MAUI's cold start on an
      overloaded emulator (load average up to 57); +12 to +14 ms
      with the process alive; the first alarm after the emulator's
      first boot was 35 s late. After `adb reboot`, a future alarm
      was re-registered and fired, an overdue one fired as LATE, and
      an unanswered notification came back. Complete, Snooze and
      Schedule each worked, started cold by the action; "Clear all"
      was detected as a swipe and the alarm returned 30 s later; an
      update ran the restore; `adb pull` read the log. A screenshot
      showed the three actions.
    - Not checked: anything on the owner's phone (every M2
      acceptance criterion is still open); a real swipe gesture
      (`input swipe` registered as taps on the emulator); Doze
      reached by stillness; Android versions other than 15;
      manufacturer battery rules; a .NET 10 build; the Share button.
  - For PR #6 after the security review, on 2026-11-15:
    - `dotnet build` is clean and `dotnet test` passes, in Debug
      and Release: 545 tests (95 core, 450 web). Each of the three
      new commits builds and passes on its own.
    - New tests: `create`, `list` and `revoke` on a missing file
      refuse and leave the folder empty; on a database one
      migration behind they refuse and the file is byte for byte
      the same. `Tokens_AreNeverLogged` runs with every category
      set to Trace by settings, sends tokens in the query string,
      and fails if the code filter is removed (tried). Probes
      without a token: HEAD, OPTIONS and TRACE on `/api` routes,
      other methods on `/healthz` and `/openapi/v1.json`, unmapped
      paths in and outside `/api`, `/openapi/v2.json`, and upper
      case and trailing slashes: all 401, except the upper-case and
      trailing-slash spellings of the two anonymous endpoints,
      which route to them (200); nothing is written.
    - Docker, with every log category at Trace: in the running
      container `token create` wrote to `/data/nudge.db` and the
      token worked; with `ConnectionStrings__Nudge` unset (in
      `/app` and in `/tmp`) and with a relative path from `/`, it
      refused, exit 1, and no database file appeared. A token in
      the query string was 401 and did not appear in the log.
    - Not checked: `fly ssh console` itself, and the command run
      while the deployed app is starting.
  - For PR #6, on 2026-11-14 (the "fresh database" checks below
    describe the behaviour replaced by 48):
    - `dotnet build` is clean and `dotnet test` passes, in Debug
      and Release: 510 tests (95 core, 415 web), up from 435. The
      CI steps (restore, Release build, tests without rebuilding)
      were run locally. Each of the four commits builds and passes
      on its own.
    - Thirteen safeguards were broken in turn (no fallback policy,
      a revoked token accepted, token rows in the change log, undo
      allowed on the token table, the token logged, the source
      without the token's name, another anonymous endpoint, a token
      read from the query, a different 401 for a bad token, more
      than the token on stdout, names reusable after a revoke, the
      hash in `token list`, the token stored instead of its hash).
      A test fails for each.
    - The Release build was run on a scratch database: `token
      create` on a fresh database (before the app had ever started)
      migrated it; with the token a read was 200 and a write 201,
      recorded as `api:<name>`; without it 401 with
      `WWW-Authenticate: Bearer`; after `token revoke` 401; a second
      revoke exited 1; the app's log held neither the token nor the
      header.
    - The Docker image was built and run with a volume on `/data`.
      `docker exec <container> dotnet /app/Nudge.Web.dll token
      create ...`, run in the running container, from `/app` and
      from `/`, wrote to `/data/nudge.db`; the API answered 200 and
      201 with the token, 401 without it and after the revoke.
    - Not checked: `fly ssh console` (in particular that its
      session has `ConnectionStrings__Nudge` from the image; the
      `Database:` line shows it), the image under Litestream with a
      second process writing, the deploy, and any real client.
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
