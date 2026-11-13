# Vision

## What This Is

Nudge is a single-user, GTD-style to-do system that replaces Todoist.
It tracks one-off and repeating items, reminds me on my Android phone
in a way that cannot be silently lost, offers a web UI and an Android
app, and exposes an API so the planning-agent project can read and
change my tasks safely.

## What This Is Not

- Not multi-user. No sharing, collaboration, assignments or comments.
- Not a Todoist clone. No karma, boards, filters language, templates
  or natural-language date parsing beyond the basics.
- Not a calendar. It reads nothing from and writes nothing to Google
  Calendar; the planning agent does that.
- No reminders by email or desktop. The phone is the only channel.
- No iOS app.

## Core Principles

- **A reminder is never lost.** Swiping a notification away is not an
  answer. A reminder stays unacknowledged, re-notifies, and is listed
  under "Missed reminders" until it is completed, scheduled or
  snoozed.
- **No silent failures.** Every reminder records scheduled, fired,
  shown and acknowledged. If the phone does not confirm a due
  reminder, the server sends a backup push. A health page shows what
  is wrong.
- **Data cannot be wrecked through the API.** Rescheduling moves one
  occurrence and never alters the repeat rule or its reminders.
  Deletes are soft. Every change is logged and can be undone. Agent
  writes support dry-run and idempotency keys.
- **Recurrence is correct and boring.** Completing a repeating item
  once advances it exactly once. Setting a due date never removes the
  repeat rule.
- **One language.** C# everywhere so the owner can read, review and
  debug all of it.

## Architecture Overview

| Piece | Choice |
| --- | --- |
| Domain | `Nudge.Core`: items, projects, recurrence, reminders |
| Backend | ASP.NET Core minimal API, EF Core, SQLite |
| Web UI | Blazor, served by the same app |
| Shared UI | Razor class library used by web and Android |
| Android | .NET MAUI Blazor Hybrid + native alarm code in C# |
| Agent API | REST with OpenAPI, plus an MCP server |
| Auth | Google sign-in, one allowlisted account; API tokens |
| Hosting | Fly.io, SQLite on a volume, Litestream backup |

### Reminders

The phone schedules each upcoming reminder locally as an exact alarm
(`AlarmManager.setAlarmClock`), so it fires offline and in Doze.
Alarms are re-registered after reboot. The server sends a push only
to say "data changed, re-sync", and as a backup when the phone has
not confirmed a due reminder. Notification actions: Complete,
Schedule, Snooze.

### Recurrence

Each repeating item stores an RFC 5545 recurrence rule plus a mode:

- **Fixed schedule:** "every two weeks on Friday" stays on Fridays
  regardless of when it is completed.
- **After completion:** "every six months" counts from the day the
  item was actually completed.

### GTD model

Inbox, next actions, nested projects, contexts (labels), waiting-for,
someday/maybe, defer-until dates, priorities, a weekly review view,
and a per-item "do not auto-reschedule" flag.

## How to Run

Install: `dotnet restore`
Run: `dotnet run --project src/Nudge.Web`
Test: `dotnet test`

## Open Questions

- Re-notify interval for unacknowledged reminders, and whether it
  should escalate.
- Whether MAUI proves reliable for exact alarms on the owner's phone
  (decided by the alarm spike; fallback is a Kotlin app).
- Offline editing on the phone: read-only cache first, or full
  offline writes with sync.
