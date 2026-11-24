# Status

**Last updated:** 2026-11-24
**Current milestone:** M2 Alarm spike (M3 Web UI is `done`)

## In Progress
- M1. All six tasks are on main: PR #6 was merged on 2026-11-15
  with a merge commit (359a633), which brought #2, #3, #4 and #5
  with it. M1 is `done` as of 2026-11-15: the owner approved the
  deploy, made the first API token on the server, and called the
  deployed API with it (200).
  - CI passed on #6 at 97d9e48 before the merge. Nobody reviewed
    #4, #5 or #6: CodeRabbit skipped them. Its three comments on
    #2 and #3 are fixed in the merge but not answered on GitHub
    (see "Blockers / Open Questions").
- The owner did not follow the work of 2026-11-13, 2026-11-14 and
  2026-11-15 as it was done. Everything needed to review it is in
  this file, in the six pull request descriptions, and in the
  DECISIONS.md entries of those three dates.
- M2, the Kotlin alarm spike: built, PR #8 is open and not merged
  (branch `m2-kotlin-alarm-spike`). On 2026-11-22 main was merged
  into it (b6a2b91). Only DECISIONS.md conflicted, and both sides
  were kept. CI passes at b6a2b91 and the PR merges cleanly. The
  release APK builds from it.
  https://github.com/kpanko/nudge/pull/8
  CodeRabbit left three minor comments, fixed in 017da13 and
  answered on the pull request (see 67 and 68 under "Blockers /
  Open Questions"). One thread is still open: another app could
  send `QUICKBOOT_POWERON` to the exported boot receiver, causing a
  false boot log line and a restore. This was not tested. The owner
  has to accept that for a throwaway spike or ask for a fix. The
  owner said on 2026-11-15 they will review the PR before it is
  merged; it has not been approved yet.
  The first M2 task is ticked in MILESTONES.md on that branch, not
  on main yet. The second task is the owner's: sideload the APK
  and run the checklist in `spikes/AlarmSpikeKotlin/README.md` on
  the phone. Every M2 acceptance criterion is still open until
  then. See "Recently Completed" and "Key Context" for what the
  spike is and what was checked.
- M3, the web UI: built on 2026-11-15 and 2026-11-16 as three
  stacked pull requests, #9, #10 and #11, all merged on 2026-11-22.
  #9 went to `main`, but #10 and #11 went into `m3-ui-shell-auth`,
  so the first deploy had only the sign-in shell. At the owner's
  request that branch was merged directly into `main` (0b0e78c),
  without another pull request. The build was clean and 678 tests
  passed locally on the merge; the browser test runs in CI. The
  repository now deletes branches after a merge.
  https://github.com/kpanko/nudge/pull/9
  https://github.com/kpanko/nudge/pull/10
  https://github.com/kpanko/nudge/pull/11
  The owner approved the deploy. M3 is `done` as of 2026-11-22: on
  the deployed app the owner signed in with Google, then set
  `Nudge__Owner__Email` to another address and was refused, then
  restored it and signed in again. No second Google account was
  used. Choices 69 to 102
  under "Blockers / Open Questions" and the DECISIONS.md entries of
  2026-11-16 explain the design; each pull request description has
  what was checked and its review comments.
- Deploy jobs for STATUS.md-only commits on main (5608b0d, c26fcc5,
  dd6e2c1 and later) wait for approval and need not be approved.
- PR #12, "Add a verify skill that drives the real app", is open
  and was not tracked here before 2026-11-24. Not looked at in that
  session.
  https://github.com/kpanko/nudge/pull/12
- Before the Kotlin spike: on 2026-11-15
  the owner decided the Android app will be Kotlin, offline-first
  (see DECISIONS.md under that date), so the spike was
  rebuilt as a bare Kotlin app. The MAUI spike, PR #7, was closed
  without merging; its branch `m2-alarm-spike` still exists, and
  its checklist in `spikes/AlarmSpike/README.md` there can be
  reused. Everything further down in this file about the MAUI
  spike describes that closed branch.
  https://github.com/kpanko/nudge/pull/7

### The deploy
The merge to main started a deploy that waits for approval of the
`production` environment. Startup then applies the five migrations
to the production database at `/data/nudge.db`, which is empty so
far. An older run on main, from 2026-11-13, is also waiting for
approval; it is for the commit before the merge, so do not
approve it.

### Must be done by hand
- Done on 2026-11-15: the owner's time zone is staged as a Fly
  secret and applies on the next deploy. The command, with the
  owner's IANA id (`Europe/London` is only an example):

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
- Done on 2026-11-15 (the owner made the `planning-agent` token
  and a call with it returned 200). For another token: after the
  first deploy that contains #6, make a token on the
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
- M3 sign-in: all done on 2026-11-22; kept for a rebuild. Until
  these are done the web UI stays locked ("Sign-in
  is not configured") and the API is unaffected:
  1. In Google Cloud Console, create an OAuth client of type "Web
     application" with the authorized redirect URI
     `https://nudge-gtd.fly.dev/signin-google`.
  2. Set the secrets (the values are the owner's; nothing is
     committed):

     ```bash
     fly secrets set --app nudge-gtd \
       Nudge__Google__ClientId=... \
       Nudge__Google__ClientSecret=... \
       Nudge__Owner__Email=...
     ```

     Optionally also `Nudge__Owner__Subject`, the account's Google
     id, to pin it further.
  3. Nothing to set for the Data Protection keys:
     `Nudge__DataProtection__KeysPath = "/data/keys"` is in the
     `[env]` section of `fly.toml` in #9.
  4. Approve the deploy.
  5. Sign in at https://nudge-gtd.fly.dev/ with the owner's
     account, and check that another account is refused.

## Recently Completed
- Web UI improvements (2026-11-23 and 2026-11-24): six stacked pull
  requests, #13 to #18, merged on 2026-11-24 by merging #18 with a
  merge commit (a158067), and deployed with the owner's approval.
  #13 phone layout and visual style; #14 notice and Undo stay in
  view; #15 per-row Organize in the inbox; #16 plain-language
  dates, repeats and reminders; #17 capture from every page; #18
  Google's account picker on "sign in again".
  https://github.com/kpanko/nudge/pull/18
  - Before merging, all seven CodeRabbit threads (on #13, #14, #16
    and #17) were fixed on the branch where each started and merged
    forward, each reproduced first by a test: the phone changes
    table keeps its column names for screen readers (91bd5bd);
    project chip links are 44 px high on phones (91bd5bd); the
    scroll padding covers the tallest notice and the notice text
    takes keyboard focus for Safari (04e9795); a capture sent twice
    from a deleted page lands on the inbox both times (be63d43).
    The threads were not answered or resolved on GitHub.
  - 910 tests passed locally on #18, browser tests included; CI
    passed on every branch and on the merge.
  - The owner confirmed on 2026-11-24 that sign-in works on the
    deployed app, account picker included. The owner does not want
    the layout tried on a real phone; only Chromium emulation, no
    Safari, Firefox or screen reader, was used.
- Kotlin alarm spike (PR #8, open), the first task of M2.
  `spikes/AlarmSpikeKotlin` is a standalone Gradle project, outside
  `Nudge.sln`, that CI never builds: Kotlin, platform APIs only (no
  Compose, no AndroidX), Android 8.0 and later, target API 35. It
  carries over the MAUI spike's behaviour, permissions, log format
  and phone checklist: test alarms with `setAlarmClock`, a
  notification with Complete, Schedule and Snooze, a swipe brings
  the alarm back after 30 s, and a boot receiver registers pending
  alarms again. Build, from the repository root, with a JDK 17 and
  the Android SDK (API 35):

  ```bash
  cd spikes/AlarmSpikeKotlin && ./gradlew assembleRelease
  ```

  The APK is
  `spikes/AlarmSpikeKotlin/app/build/outputs/apk/release/app-release.apk`
  (about 640 kB, signed with the local debug key). It has the same
  application id as the MAUI spike, so uninstall that one first if
  it is on the phone.
- MAUI alarm spike app (PR #7, closed without merging). `spikes/AlarmSpike`
  is a MAUI app for Android, outside `Nudge.sln`: it schedules test
  alarms with `AlarmManager.SetAlarmClock`, shows a notification
  with Complete, Schedule and Snooze, notices a swipe and brings the
  alarm back, and registers pending alarms again after a reboot. It
  was built and run on an emulator only; the phone test is M2's
  second task and is the owner's.
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
1. M2: review and merge PR #8 (merging starts the gated deploy
   job, though nothing in the server changed), then sideload the
   APK and run the checklist on the phone, filling in the results
   table in the spike's README. When the three open M2 criteria
   pass, tick them and the second task, and mark M2 `done`.
2. M4, the Todoist import. M3 builds on the M1 design in #2 to #6,
   which the owner has not reviewed yet, and it changed M1 code:
   `WriteRunner` is split into a core and the API's adapter (#10).
3. Look at PR #12 (verify skill), which no session has tracked.
4. Confirm the two choices in the CodeRabbit fixes of 2026-11-15
   (see "Blockers / Open Questions"); both can still be changed by
   a new commit on main.
5. Keep the `planning-agent` token for the MCP server (M6);
   nothing reads it until then.

## Blockers / Open Questions
- No blockers.
- Decided on 2026-11-15: the owner chose a merge commit for #6,
  so commit 11a1267, which wrote the APK's full path on the dev
  machine, user folder included, into this file, is now in main's
  history. 33e8014 replaced the line with a path inside the
  repository.
- CodeRabbit's three comments on #2 and #3 are fixed by two
  commits (2026-11-15), merged with #6 but not answered on GitHub.
  Both choices are in DECISIONS.md under that date.
  - #2, `Item.cs` line 175: `Repeat` on a finished item is now
    refused (`InvalidOperationException`, 409 from the API, dry
    runs too); undo the completion first. **Confirm:** refuse rather
    than reopen.
  - #3, `ItemConfiguration.cs` line 65 and `ProjectConfiguration.cs`
    line 23: a test reproduced it. Removing a tracked project
    nulled the tracked item's project and the child's parent in the
    change tracker; the save was still refused by the change log's
    no-removal rule, so the database was untouched. Both now use
    `ClientNoAction`, and the new migration
    `KeepTrackedDependentsOnProjectRemoval` rebuilds `Items` and
    `Projects` (foreign keys `RESTRICT` to `NO ACTION`; a delete of
    a referenced row is still refused). **Confirm:** the extra
    migration is acceptable before the first deploy.
  Still to do: reply to the three comments on #2 and #3. The new
  migration was tested on in-memory SQLite only, not on a file
  database.
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
  - In the Kotlin alarm spike (PR #8), on 2026-11-15. The newest
    DECISIONS.md entry, on that branch, explains these:
    61. No Compose and no AndroidX: only the Android Gradle Plugin
        and the Kotlin plugin, so the result is about Android
        itself. M5 will use Compose.
    62. Gradle 8.11.1, AGP 8.7.3, Kotlin 2.0.21. The Gradle wrapper
        is committed with its checksum pinned. Gradle itself was
        not installed on this machine: it was downloaded to a
        temporary folder, which may be gone; `./gradlew` downloads
        it again when needed.
    63. Copy log and Share log send the last 500 lines as text, not
        a file; `adb pull` gets the whole log.
    64. The event log never throws. While shared storage is not
        ready, lines go to `pending.log` in private storage and are
        moved into `events.log` at the next write. An independent
        review found that a failed log write could otherwise crash
        the app before the notification was shown.
    65. On Android 12 and 12L, alarms are not registered again by
        themselves after exact alarms are revoked and allowed
        again; the README says to press "Re-register all".
    66. Lint's three warnings (backup rules, the held app context,
        a view constructor) are left as they are, not suppressed.
    67. The boot receiver stays exported, against CodeRabbit's
        suggestion of `exported="false"`, so that no manufacturer's
        boot broadcast is shut out; it ignores every action but
        `BOOT_COMPLETED`, `MY_PACKAGE_REPLACED` and
        `QUICKBOOT_POWERON`.
    68. A fire for a pending alarm that is not yet due (a stale
        broadcast, for example just after Snooze) is logged as
        `fired-early`, not shown, and the alarm is registered
        again. CodeRabbit suggested only ignoring it. A second fire
        for an alarm already showing re-posts its notification.
  - In the web UI (PRs #9, #10 and #11), on 2026-11-15 and
    2026-11-16. The pull request descriptions number these 67 to
    100; here they follow on from the spike's 67 and 68. The
    DECISIONS.md entries of 2026-11-16 explain them.
    69. Static server-rendered Blazor; changes recorded as `web`.
    70. Owner matched by verified email, ignoring case; optional `sub`
        pin; re-checked on every request.
    71. Missing sign-in settings: the app starts, the UI is locked, a
        warning is logged.
    72. No development sign-in; tests use a fake Google through the
        real OpenID Connect handler.
    73. The cookie is never accepted by the API; the UI has a separate
        `Owner` policy.
    74. `X-Forwarded-Proto` trusted from any sender (Fly's proxy only).
    75. Data Protection keys as files in `/data/keys`; in memory when
        unset.
    76. Problem-JSON status pages only on the API side; the UI has a
        not-found page, done by middleware.
    77. Guard tests split into an API side and a UI side.
    78. A non-GET request to an unknown UI path gets 401 from the
        fallback policy.
    79. Signing out only deletes the cookie; no server-side session
        list.
    80. `WriteRunner` split into a core and the HTTP adapter; the
        handlers are internal.
    81. Each web write runs in its own DI scope; page reads are
        untracked.
    82. Plain SSR forms, a `@formname` each, reading the posted fields.
    83. Form key `web-<guid>`, fingerprinted from the posted form; the
        same key with other values is refused.
    84. After a write, a redirect with `?change=N` and an Undo notice.
    85. `/` is the inbox.
    86. Processing is one edit: list, project and defer date.
    87. `/next`: an item under each of its contexts, "No context" last.
    88. "Projects without a next action" counts the project's own items
        only.
    89. The weekly review is read-only; "next seven days" means today
        and the six after it.
    90. Missed reminders: a past `FireTime` on an open item,
        provisional until M5.
    91. The UI adds reminders only as minutes from the due time.
    92. `/changes` shows the latest 50, each with Undo.
    93. Test sign-in through the fake Google, not a test-only scheme.
    94. AngleSharp 1.9.1 in the tests (1.1.2 to 1.4.0 fail on an
        advisory).
    95. M3 marked `in-progress`; operations and views ticked.
    96. The smoke test has a project of its own; shared test files are
        linked.
    97. The smoke test runs over HTTPS with a certificate made for the
        run (plain HTTP gets an antiforgery 500).
    98. The fake Google's sign-in page is served by a test startup
        filter.
    99. Browser tests are skipped unless `NUDGE_PLAYWRIGHT=1`.
    100. Microsoft.Playwright 1.63.0.
    101. `FakeGoogle` has its own `ClientId`.
    102. The sign-in cookie is checked against the configuration as it
         is at each request, and sign-in must also have been configured
         at startup.
- The open questions in VISION.md are unchanged.

## Key Context
- Cloud sessions are told to push only to their own `claude/...`
  branch unless the owner says otherwise. On 2026-11-24 the owner
  said to push review fixes to the pull request branches themselves.
- Deploys wait for the owner's approval of `production`; Claude's
  GitHub tools cannot approve them. A newer run on main cancels an
  older pending one, so do not push to main while a deploy the
  owner wants is waiting.
- What was and was not checked:
  - For the web UI (PRs #9, #10 and #11), on 2026-11-16:
    - `dotnet build` (warnings are errors) and `dotnet test` pass:
      623 tests on #9's branch (97 core, 526 web), 678 on #10's
      (97 core, 581 web), 679 on #11's with the browser test on
      (97 core, 581 web, 1 browser).
    - Through the real OpenID Connect handler, against a fake
      Google: the owner is accepted (address case ignored); another
      account, an unverified or missing address, a token for
      another client, another issuer, and a callback with no
      sign-in started are refused with no cookie. A cookie is shut
      out by a change of owner, a pinned subject that differs, or
      sign-in no longer configured, also while the app runs.
    - The cookie opens no API endpoint, and an API token opens no
      page. Every UI endpoint but the listed sign-in pages and the
      static files needs the owner.
    - Every write from the UI is logged as `web` with the API's
      description and can be undone from its notice. A form sent
      twice writes once and is answered the same, also once the
      form is gone. Antiforgery is checked, item text is
      HTML-encoded, and completing a repeating item advances it
      once.
    - The Chromium smoke test passes locally on Windows and in CI
      (run 43979071913 at 33c4863: Passed 1, Skipped 0). It uses
      enhanced navigation and forms throughout, and fails if any
      step is a full page load (tried).
    - Three independent reviews (one per pull request); their
      findings are fixed. CodeRabbit reviewed all three pull
      requests: one finding on #9 (fixed in ca92d55), five on #10
      (fixed in 6f4e30b) and a follow-up there about fractions of a
      second in due times (fixed in 19c4780), and one on #11 (fixed
      in 33c4863). With the owner's go-ahead, every CodeRabbit
      thread on #9, #10 and #11 was answered and resolved on
      2026-11-16; each pull request description lists them under
      "Review comments". One later CodeRabbit follow-up on #11 was
      left unfixed on purpose: a disk-write failure while the smoke
      test saves its failure artifacts can still replace the test's
      own failure, which is not worth more code in a diagnostics
      path (listed under known limits in #11).
    - Checked on the deployed app on 2026-11-22: real Google
      sign-in, and refusal when the signed-in account is not the
      configured owner.
    - Not checked: a cookie surviving a restart (the keys in
      `/data/keys`); browsers other than Chromium, as automated
      tests; the UI on a phone. M3 builds on the M1 design the
      owner has not reviewed, and the `WriteRunner` split changed
      M1 code.
    - Known limits: `dotnet run` on the `http` launch profile shows
      a 500 on every page, because antiforgery cookies are Secure
      only; use the `https` profile. Signing out only deletes the
      browser's cookie. `X-Forwarded-Proto` is trusted from any
      sender. The weekly review and missed reminders definitions
      are provisional until M5.
  - For the Kotlin alarm spike (PR #8), on 2026-11-15:
    - `./gradlew assembleRelease lint` succeeds with no Kotlin
      warnings (they are errors) and no lint errors, also from a
      fresh checkout of the first commit in a temporary worktree.
    - On the Pixel 7 API 35 emulator (run with `-read-only`, more
      memory and software graphics, so the AVD is unchanged), with
      the Release APK of the first commit: app open +402 ms; after
      `am kill` with the screen off +2173 ms (cold start); forced
      deep Doze +64 ms (the phone left Doze under a minute before
      the alarm, and the process was alive); airplane mode after
      `am kill` +283 ms; after `adb reboot` a future alarm was
      re-registered and fired at +339 ms, and an overdue one fired
      as LATE; Complete, Snooze and Schedule each worked, Complete
      and Snooze also with the process killed first; "Clear all"
      was detected as a swipe and the alarm came back 30 s later;
      an update ran the restore; `adb pull` read the log.
    - After the review fixes, a shorter run on the final APK: cold
      start after `am kill` +2283 ms; after `adb reboot` one
      notification was re-shown and one alarm re-registered, which
      fired at +711 ms; log lines written before shared storage
      was ready were in `events.log` afterwards; Copy log worked.
    - After the CodeRabbit fixes (017da13), build and lint again,
      and on the emulator: a normal alarm +553 ms; Snooze, then
      +87 ms, with no `fired-early` line; after `adb reboot` a
      future alarm was re-registered and fired at +213 ms; an
      explicit broadcast with an unknown action to the boot
      receiver logged and restored nothing. The `fired-early` path
      itself could not be provoked from adb and was checked by
      reading only.
    - Not checked: anything on the owner's phone (every M2
      acceptance criterion is still open); a real swipe gesture
      (`input swipe` did not dismiss the notification on the
      emulator); Doze reached by stillness; Android versions other
      than 15; manufacturer battery rules; the full checklist on
      the final APK (only the shorter run above); CI does not build
      the spike. On the emulator `BOOT_COMPLETED` reached the app
      1.5 to 3 minutes after the system finished booting.
  - For the MAUI alarm spike (PR #7, closed), on 2026-11-15:
    - The Release build from the command line is clean (no
      warnings). The APK path is under "Must be done by hand".
      Repeated from a fresh checkout of c8230e4 in a temporary
      worktree, restore included: no warnings, 3 min 21 s, and an
      APK of the same size as the first.
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
- Android toolchain on the dev machine: the Android SDK (API 34
  and 35, build-tools 35.0.0, a Pixel 7 API 35 emulator) and a
  JDK 17, both as Visual Studio installed them, under
  `C:\Program Files (x86)\Android`. Neither `java` nor `adb` is on
  PATH and `ANDROID_HOME` is unset; the spike's README says what to
  set. Android Studio and Gradle are not installed.
- MAUI and Android workloads are installed for .NET 9; the .NET 10
  workloads have not been checked.
- `dotnet workload list` hangs on this machine; avoid it.
- Pushes to main deploy after approval of the `production`
  environment in GitHub Actions. The local
  flyctl is old and failed to allocate IPs on first deploy; they
  were allocated by hand.
- Backup restore, rehearsed on the production machine on
  2026-11-22, into a temporary folder that was then deleted; the
  live database was not written to:

  ```sh
  litestream restore -o /tmp/rr/nudge.db /data/nudge.db
  ConnectionStrings__Nudge=Filename=/tmp/rr/nudge.db \
    dotnet /app/Nudge.Web.dll token list
  ```

  Run them in `flyctl ssh console --app nudge-gtd` after
  `mkdir /tmp/rr`, and `rm -rf /tmp/rr` afterwards.
  - Checked: the snapshot and WAL files were applied; the copy is
    163,840 bytes, as is the live main file; the app opened it,
    found no pending migration, and listed the `planning-agent`
    token. Replication lag was about 0.5 s, and the replica's last
    write matched the live WAL's time (02:11 UTC).
  - Not checked: a marker row written just before the restore; a
    full SQLite integrity check; a real recovery, where `run.sh`
    restores by itself onto a new empty volume.
  - The Data Protection keys in `/data/keys` are not in the backup,
    so a real recovery signs the owner out.
