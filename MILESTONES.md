# Milestones

Each milestone is sized to fit roughly one working session and ends
deployed and tested. Status values: planned | in-progress | done

## M0: Foundation — `done`
**Goal:** An empty but real app: builds, tests, runs in CI, deploys.

**Acceptance criteria:**
- [x] `dotnet test` passes locally and in GitHub Actions
- [x] A health endpoint responds from the deployed Fly.io app
- [x] SQLite lives on a Fly volume with Litestream backup running

**Tasks:**
- [x] Solution with `Nudge.Core`, `Nudge.Web`, `Nudge.Core.Tests`
- [x] Health endpoint and a test for it
- [x] CI workflow: build and test
- [x] Dockerfile
- [x] GitHub repository and first push
- [x] `fly.toml`, volume, Litestream backup, first manual deploy
- [x] Deploy job in CI with an approval gate

---

## M1: Core, recurrence, REST API — `in-progress`
**Goal:** The domain is correct and reachable over HTTP.

**Acceptance criteria:**
- [ ] Completing a repeating item once advances it exactly once
- [ ] Setting a due date never removes the repeat rule
- [ ] Rescheduling one occurrence leaves rule and reminders intact
- [x] Property-based recurrence tests pass across DST changes,
      month ends and leap years
- [ ] Every write is in the change log and can be undone

**Tasks:**
- [ ] Entities: item, project (nested), context, reminder
- [x] Recurrence engine: fixed-schedule and after-completion modes
- [ ] EF Core + SQLite persistence and migrations
- [ ] Change log, soft delete, undo
- [ ] REST endpoints with OpenAPI; dry-run and idempotency keys
- [ ] API-token auth

---

## M2: Alarm spike — `planned`
**Goal:** Prove MAUI can deliver reliable exact alarms on the
owner's phone before building the real app.

**Acceptance criteria:**
- [ ] A hard-coded alarm fires on time in Doze, in airplane mode,
      and after a reboot
- [ ] Notification shows Complete, Schedule, Snooze actions
- [ ] Swipe-away is detected
- [ ] Decision recorded: MAUI or Kotlin for the Android app

**Tasks:**
- [ ] Bare MAUI Android app with `setAlarmClock` and boot receiver
- [ ] Sideload and run the checklist on the phone

---

## M3: Web UI — `planned`
**Goal:** Daily use from a browser.

**Acceptance criteria:**
- [ ] Sign in with Google; any other account is rejected
- [ ] Capture to inbox, process, complete, defer, edit recurrence
- [ ] Views: inbox, next actions by context, projects, waiting,
      someday/maybe, weekly review, missed reminders
- [ ] Playwright smoke test passes in CI

**Tasks:**
- [ ] Shared Razor component library
- [ ] Google OIDC with allowlist
- [ ] Views listed above

---

## M4: Todoist import — `planned`
**Goal:** Real data in Nudge, running beside Todoist.

**Acceptance criteria:**
- [ ] Projects (with nesting), labels, priorities, due dates,
      recurrence and reminders are imported
- [ ] Import is repeatable without creating duplicates
- [ ] Items whose recurrence cannot be parsed are listed, not dropped

**Tasks:**
- [ ] One-way importer and a report of what was skipped

---

## M5: Android app — `planned`
**Goal:** Reminders that cannot be lost.

**Acceptance criteria:**
- [ ] Sync, lists, quick add, complete, schedule, snooze
- [ ] Swiped reminders re-notify and appear in Missed reminders
- [ ] Delivery log: scheduled, fired, shown, acknowledged
- [ ] Server backup push when a due reminder is unconfirmed
- [ ] Health page: last sync, alarm, notification and battery
      permissions
- [ ] One-week soak on the phone with no missed reminder

**Tasks:**
- [ ] MAUI Blazor Hybrid shell reusing shared components
- [ ] Local alarm scheduling, boot receiver, notification actions
- [ ] Push for re-sync and backup delivery
- [ ] APK built by GitHub Actions

---

## M6: MCP server and cutover — `planned`
**Goal:** planning-agent uses Nudge; Todoist is cancelled.

**Acceptance criteria:**
- [ ] MCP tools match the names planning-agent already calls
- [ ] planning-agent runs a full planning session against Nudge
- [ ] Final import done; Todoist subscription cancelled

**Tasks:**
- [ ] MCP server over the REST API
- [ ] Switch planning-agent configuration
