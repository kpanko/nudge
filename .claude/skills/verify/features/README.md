# Nudge features to verify

Pick the features a change touches. Each file has the sub-features, how
a user gets there, journeys to paste into a launched shell (see
[../SKILL.md](../SKILL.md)), the end state including the database, and
gotchas.

| Feature | Use when a change touches | Surfaces |
| --- | --- | --- |
| [Capture and process](capture-and-process.md) | items, lists, completing, dry run, idempotency | API, DB |
| [Repeating items](repeating-items.md) | recurrence, due dates, reminders. **M1 regressions** | API, DB |
| [Projects and contexts](projects-and-contexts.md) | projects, nesting, contexts, filters | API, DB |
| [Change log and undo](change-log-and-undo.md) | any write, soft delete, undo | API, DB |
| [Access and sign-in](access-and-sign-in.md) | tokens, auth, the web UI's lock, pages | API, CLI, UI |

If unsure, run **repeating-items** and **change-log-and-undo**: nearly
every write passes through them.

The signed-in pages are listed in each feature as the user sees them.
On the launched app they are locked, so a page journey is proven only
through `nv_ui_smoke` or a Playwright test, on the test host. Record
anything else as `nv_unverified`.

Common facts:

- JSON is strict: camelCase names and enum values (`nextAction`,
  `fixedSchedule`). An unknown property is a 400.
- Every write answers `{dryRun, change, data}`.
- `Id` columns hold upper-case GUIDs and the API returns lower-case, so
  queries use `upper(Id) = upper(?)`.
- `Items.State` holds `Inbox`, `NextAction`, `WaitingFor`, `Someday`.
- Deleting is soft: the row stays with `DeletedAt` set.
