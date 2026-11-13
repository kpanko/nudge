# Decisions

<!-- Architectural Decision Records (ADRs). Add an entry whenever a significant
     design choice is made. The goal is to capture *why*, not just *what*, so
     future sessions don't re-litigate settled questions. -->

---

## 2026-11-12 C# and .NET for every component

**Context:** The owner's other personal projects are Python, but
their day job is C# and Blazor. Sessions are limited, so the owner
reviews and debugs between them.

**Decision:** ASP.NET Core API, Blazor web UI, .NET MAUI Blazor
Hybrid for Android, all sharing `Nudge.Core` and a Razor component
library.

**Alternatives considered:** Python/FastAPI + HTMX + Kotlin (matches
planning-agent, but three languages and two UIs). Capacitor-wrapped
web app (cannot run in the background to pick up changes).

**Consequences:** UI is written once. Android builds are heavier
than Kotlin. planning-agent is unaffected because it talks to Nudge
over HTTP and MCP.

---

## 2026-11-12 Phone schedules reminders locally

**Context:** Todoist reminders sometimes never arrived, with no way
to find out why, and a swiped notification was gone for good.

**Decision:** The phone registers exact alarms for upcoming
reminders. Server push is used only for "re-sync" and as a backup
when the phone has not confirmed a due reminder. Reminders stay
unacknowledged until completed, scheduled or snoozed.

**Alternatives considered:** Server-sent push only (depends on
network and vendor delivery at the moment the reminder is due).

**Consequences:** Reminders work offline. Needs exact-alarm
permission and a boot receiver, and a delivery log to make failures
visible.

---

## 2026-11-12 SQLite with Litestream on Fly.io

**Context:** One user, small data, existing Fly.io account.

**Decision:** SQLite on a Fly volume, continuously replicated with
Litestream.

**Alternatives considered:** Postgres (more to run, no benefit for
one user).

**Consequences:** Single instance only. Restores are a file copy.

---

## 2026-11-12 Google sign-in with one allowlisted account

**Context:** The owner uses Google sign-in for everything.

**Decision:** Google OIDC for web and phone, restricted to one
configured account. planning-agent uses an API token.

**Alternatives considered:** Local password (one more credential to
manage).

**Consequences:** No user table. The allowed account is deployment
configuration, not code.

---
