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

## 2026-11-13 An item keeps its due date apart from its repeat rule

**Context:** In Todoist, changing a due date could silently drop the
repeat rule. The two named M1 regression tests guard against this.

**Decision:** `Item` holds `DueDate`/`DueTime`, a `Recurrence` and an
`OccurrenceDate` (the day the rule placed the current occurrence
on). `SetDue` touches only the due date and time. The rule changes
only through `Repeat` and `StopRepeating`. `Complete` advances from
`OccurrenceDate` and leaves a repeating item open; it finishes the
item only when it does not repeat or the series has ended.
`ClearDue` is refused on a repeating item. Completing an item that
is already finished throws.

**Alternatives considered:** One "set due" call taking an optional
rule (the Todoist shape; omitting the rule is how rules got lost).
Letting a repeating item have no due date (nothing to show or
remind on).

**Consequences:** The API needs separate operations for "reschedule"
and "change how it repeats". Retried requests are the API's job
(idempotency keys); the domain advances once per `Complete` call.

---

## 2026-11-13 Defer date moves with a repeating item

**Context:** A repeating item deferred until two days before it is
due would otherwise reappear at once after being completed.

**Decision:** On completion the defer date keeps the same number of
days ahead of the new occurrence as it had ahead of the old one,
measured from the rule's day, not a moved due date. A defer date
later than the occurrence is dropped.

**Alternatives considered:** Clearing it (item shows immediately).
Leaving it (same effect, and stale).

**Consequences:** "Defer" on a repeating item acts as a lead time.

---

## 2026-11-13 Reminders are an instant or an offset from the due date

**Context:** Rescheduling must never alter reminders, yet a reminder
has to follow the item to its new day.

**Decision:** A `Reminder` is either a fixed instant or a signed
wall-clock offset from the due date and time. A due date without a
time counts as 00:00, so +9h is "09:00 on the day" and -6h is
"18:00 the day before". The offset is added on the wall clock and
then resolved by `LocalTimeResolver`, so it survives clock changes.
Offsets are limited to 366 days. A relative reminder on an item
with no due date, and any reminder on a finished item, has no fire
time.

**Alternatives considered:** Storing computed fire times (must be
rewritten on every reschedule). A separate "days before plus time of
day" kind (a second mechanism for what a signed offset already
does).

**Consequences:** Rescheduling and repeating never write to
reminders. A fixed-instant reminder on a repeating item fires once.
Delivery state (scheduled, fired, shown, acknowledged) is not
modelled yet; that is M5.

---

## 2026-11-13 EF Core in Nudge.Web, migrations applied at startup

**Context:** `Nudge.Core` has to stay free of storage code. There is
one app, one SQLite file and one process using it.

**Decision:** `NudgeDbContext` and its mappings live in
`Nudge.Web/Data`, on EF Core 10, the newest line that runs on
.NET 10; it brings Microsoft.Data.Sqlite with it, so the two cannot
be on different versions. Migrations are generated with the
`dotnet-ef` tool pinned in `dotnet-tools.json`, committed, and
applied by the app when it starts, before it serves anything.
Startup first removes any migration lock left by a process that was
killed, because EF Core would wait for it for ever.

**Alternatives considered:** A separate `Nudge.Data` project (two
more projects with no second user of them yet). Applying migrations
as a deploy step, by bundle or script (one more step to forget, and
nobody but the owner to review the SQL).

**Consequences:** A migration that fails stops the app at startup,
where the Fly health check shows it. A test fails if the model and
the migrations disagree. A merged migration is never edited; a new
one is added. EF Core moves to 11 only together with .NET 11.
Removing the lock is only safe while a single process uses the
database, and depends on EF Core's lock table; a test covers it.

---

## 2026-11-13 Stored values are readable and sort correctly

**Context:** The owner must be able to read the database with any
SQLite tool, and queries must be able to sort and compare in SQL.

**Decision:**
- Ids are made by the entities (version 7 GUIDs) and stored as
  text. The database never generates one.
- Instants are UTC text of fixed width,
  `2026-11-13T04:30:00.0000000Z`. The offset they were created with
  is not kept.
- Dates are `2026-11-13`, times of day `14:30:00`, a reminder's
  offset `-00:30:00`.
- `ItemState` and `RecurrenceMode` are stored by name. `Priority` is
  stored as its number, because it is a scale and has to sort.
- How an item repeats is three columns on the item (the rule as
  RRULE text, the mode, the start day), all null if it does not.

**Alternatives considered:** The SQLite provider's own format for
instants (local time with an offset: it does not sort, and EF Core
refuses to compare it in SQL). Numbers for every enum (unreadable,
and reordering an enum would change what rows mean). One JSON
column for the repeat rule (harder to read and to constrain).

**Consequences:** Instants are read back in UTC. Renaming a member
of `ItemState` or `RecurrenceMode`, or renumbering `Priority`, needs
a migration that rewrites the rows; a test pins the formats. The
entities refuse enum values that are not defined, so EF Core's own
conversions can be used as they are.

---

## 2026-11-13 The database repeats the rules the entities keep

**Context:** "Data cannot be wrecked through the API" should not
depend on every write going through a correct entity.

**Decision:** Check constraints: an item has all of rule, mode,
start day and occurrence day, or none; no due date means no due
time and no rule; a reminder is an instant or an offset, never both
or neither. Foreign keys: a reminder goes when its item goes; a
project that still has items or projects under it cannot be
deleted. One rule is new here because an entity cannot check it
alone: context names are unique, ignoring the case of ASCII
letters.

**Alternatives considered:** Trusting the entities alone (one wrong
query or hand edit away from a row that completes for good instead
of repeating).

**Consequences:** Changing one of these rules needs a migration.
The API has to turn a duplicate context name into a clear error.
What deleting a project with things in it should do is left to the
soft-delete task; until then the database refuses it.

---

## 2026-11-13 Items are read whole, projects all together

**Context:** The entities assume they hold everything that belongs
to them. A half-loaded item looks like an item without reminders. A
project read without its parent looks like a top-level project, and
`MoveUnder` cannot see a loop through a parent it was never given.

**Decision:** Every query for items brings their project, contexts
and reminders along, in a single SQL query. Code that uses
`Ancestors` or `MoveUnder` reads all projects first; there are few.
A project or context that already exists is read through the same
`NudgeDbContext` before it is given to an item.

**Alternatives considered:** Naming what to load at each call
(forgetting is silent). Lazy loading (needs unsealed entities with
virtual members, and hides queries). Split queries (several round
trips that can each see a different state).

**Consequences:** Reading items joins four tables, which is fine at
this size. The rule for projects is a convention the database
cannot enforce: the endpoint that moves a project needs a test,
through HTTP, that a loop is refused.

---

## 2026-11-13 Four lists, four priorities; finished is not a list

**Context:** VISION.md names the GTD lists and says items have
priorities, without fixing either. Todoist has four priority levels
and nothing like the lists.

**Decision:** An open item is on exactly one list: `Inbox`,
`NextAction`, `WaitingFor` or `Someday`. A new item starts in the
inbox. Priority is `None`, `Low`, `Medium` or `High`. Being finished
is not a list: `CompletedAt` records it, and the item keeps the list
it was on. Setting a list, priority or repeat mode that does not
exist is an error, because any number can be cast to an enum and an
item on a list that does not exist would be shown nowhere.

**Alternatives considered:** A `Completed` list (the item would
forget which list it was on, which undo needs in order to put it
back). Lists the owner can define (nothing in VISION.md asks for
them).

**Consequences:** Every view of open items has to leave out items
that have `CompletedAt`. The Todoist import (M4) has to map
Todoist's four priorities onto these four and decide which list each
imported item goes on. There is no way yet to reopen a finished
item; undo needs one.

---
