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

## 2026-11-12 Own recurrence engine on a small RRULE subset

**Context:** Items store an RFC 5545 rule. A full RFC library
(Ical.Net) is large and hard to review; the owner needs to be able
to read the code that decides when things come due.

**Decision:** `RecurrenceRule` in `Nudge.Core` supports FREQ (DAILY,
WEEKLY, MONTHLY, YEARLY), INTERVAL, BYDAY, BYMONTHDAY, BYMONTH,
UNTIL and WKST. Everything else (COUNT, BYSETPOS, sub-daily
frequencies, ...) is rejected at parse time with a message. A date
is tested against the rule with a plain predicate and the next
occurrence is found by checking days one at a time.

**Alternatives considered:** Ical.Net (full RFC, but a dependency
whose edge cases we would not control). Generating occurrences
period by period (faster, more code, more ways to be wrong).

**Consequences:** Easy to read and to test. The importer (M4) must
list Todoist items whose rule falls outside the subset. Searching
by day is slow in theory but irrelevant for one user.

---

## 2026-11-12 Missing month days move to the end of the month

**Context:** RFC 5545 skips dates that do not exist, so "monthly on
the 31st" would fire seven times a year and a 29 February item
once every four years.

**Decision:** A day of the month that does not exist is moved back
to the last day of that month. This applies to BYMONTHDAY and to
the day taken from the start date. Numbered weekdays (the fifth
Friday) are still skipped when they do not exist.

**Alternatives considered:** Strict RFC behaviour (surprising for a
to-do list). RFC 7529 `SKIP=` (more syntax for one behaviour).

**Consequences:** A deliberate difference from RFC 5545 that any
export to iCalendar would need to account for.

---

## 2026-11-12 Recurrence works on dates; completion uses the rule's day

**Context:** Rescheduling one occurrence must not change the rule,
and completing once must advance exactly once, including after an
occurrence was moved or completed early or late.

**Decision:** Recurrence is computed on `DateOnly` in the owner's
time zone; the time of day is attached afterwards by
`LocalTimeResolver` (a skipped time moves forward by the gap, a
repeated time uses the first). A `Recurrence` holds rule, mode and
start date. Completing a fixed-schedule item yields the first
occurrence after the later of the completion day and the day the
rule placed the occurrence on, not its possibly moved due date.
After-completion items yield the first day at least one interval
after completion that satisfies the BY parts.

**Alternatives considered:** Advancing from the due date (an
occurrence moved earlier and completed would come round again).
Queueing every missed occurrence of an overdue item (a pile of
stale copies).

**Consequences:** An item must store the rule's occurrence day
separately from its due date. Daylight-saving changes cannot move
an item to another day.

---
