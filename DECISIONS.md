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

## 2026-11-13 Every save is recorded by the context, row by row

**Context:** "Every change is logged and can be undone" has to hold
for every write there will ever be, including the ones nobody has
written yet. Writes come from the owner and from an agent.

**Decision:** `NudgeDbContext` records what it saves. One save is
one `Change` (number, time, `Source`, `Description`) with one entry
for each row added, changed or removed, written in the same
transaction as the rows. An entry holds the table, the row's key and
the values before and after, as JSON keyed by column name, each
value in the form the column holds. It is worked out from EF Core's
change tracker, not from the entities, so a new table or column is
logged without anything being added. Saving is refused until
`Source` says who is saving. The change number is the one id the
database makes: it gives the order changes were saved in, which a
clock or a random id cannot be trusted to do.

**Alternatives considered:** An event raised by each operation (a
new operation that forgets to raise one leaves a silent gap).
Triggers in SQLite (they would catch writes that go round the app,
but they are SQL to keep in step with every column, and cannot say
who made the change). One log row for each changed column (more
rows, nothing gained).

**Consequences:** Only saving through the context is logged:
`ExecuteUpdate`, `ExecuteDelete` and hand-written SQL must not be
used on these tables. The values before a change are the ones the
context read, so a request that reads, waits and writes while
another one writes in between logs what it believed, not what was
there; the endpoints should take the write lock before they read.
The log holds the same content as the tables, in the same file and
the same backup, and is never pruned. A test fails if a column is
not logged.

---

## 2026-11-13 Deleting marks a row; nothing the owner made is removed

**Context:** "Deletes are soft." The storage task also left open
what deleting a project with things in it should do.

**Decision:** Items, projects and contexts have `DeletedAt` and a
`Delete` method that sets it. The context refuses to remove their
rows, so there is no other way to delete one. Queries leave marked
rows out unless they ask with `IgnoreQueryFilters`. Deleting a
project marks everything under it at the same time, in one change:
its sub-projects at any depth and the items in them
(`DeleteProjectAsync`). Deleting a context leaves its links to items
in place, hidden, so the context is back on those items if the
deletion is undone; meanwhile its name can be used again. Reminders
of a deleted item do not fire. Reminders and the links between items
and contexts have no mark: taking one off an item removes the row,
and the change log keeps what it held.

**Alternatives considered:** Refusing to delete a project that still
has things in it (safe, but tedious, and deleting can be undone
anyway). Treating everything under a deleted project as deleted
without marking it (one row to write, but every query, and later the
phone, would have to follow the chain of parents to know whether an
item is deleted). A "deleted" list (the item would forget which list
it was on).

**Consequences:** A view of deleted things has to ask for them.
Nothing is ever really removed; emptying the trash is not built.
Only `DeleteProjectAsync` takes a project's contents with it:
calling `Project.Delete` on its own leaves its items showing with no
project, and nothing checks for that when saving. An undo that would
bring a deleted context back is refused while another context has
its name.

---

## 2026-11-13 Undo puts rows back in SQL and is itself a change

**Context:** Anything in the log has to be undoable, including what
an agent did an hour ago, after the owner has changed other things.
The entities deliberately have no way to set, say, an old occurrence
date.

**Decision:** `UndoAsync` takes one change back by returning each
row it touched to the values the log gives: changed columns get
their old values, a removed row is inserted again, an added row
goes. Adding an item, project or context is taken back by marking it
deleted, because other rows may have come to depend on it. It is all
or nothing, in one transaction, and refused when

- a value it would put back has been changed again since (checked
  column by column, so later changes to other columns do not
  matter);
- the database's own constraints object; or
- afterwards something not deleted would belong to something
  deleted, or a project would be under itself, and was not before.

The undo is recorded as a new change that names the one it took
back, so it can be undone in turn. A change cannot be undone twice
while its undo stands.

**Alternatives considered:** Undoing through the entities (each
would need a back door that sets any field, which is what they were
built not to have). Only ever undoing the latest change (useless
once owner and agent take turns). Overwriting on conflict (loses the
later change without saying so).

**Consequences:** Undoing a completion is how a finished item is
reopened, and undoing a deletion is how something is restored; there
is no separate operation for either. The SQL is built from table and
column names in the log, so they are checked against the model
first. An entry made before a column was renamed or dropped, or
before the way a value is stored was changed, is refused with a
message rather than undone; a migration that does such a thing
should rewrite the log entries too if they still need to be
undoable.

---

## 2026-11-14 The owner's time zone is required configuration

**Context:** `Item.Complete` needs the owner's time zone to know what
day an item was completed on, and so which occurrence comes next. A
wrong zone moves items by a day without anyone noticing. Where it is
configured was left open.

**Decision:** `Nudge:TimeZone` (environment variable
`Nudge__TimeZone`) holds an IANA id. It is read and checked when the
app starts, before anything is served: outside Development a missing,
unknown or non-IANA value stops the app with a message saying what
to set. In Development an unset value falls back to the machine's
zone, and the startup log names the zone in use. The repository is
public, so the owner's zone is not committed: it is set on Fly as a
secret.

**Alternatives considered:** A default such as UTC (completions in
the evening would land on the next day, silently). A zone sent by
each client (one owner has one zone; a client that forgot it would
do the same damage). The zone in `fly.toml` (public).

**Consequences:** The first deploy of this code fails at startup
until the secret is set. Tests always set a zone with daylight
saving, America/New_York.

---

## 2026-11-14 One route group; every write goes through one runner

**Context:** "Every change is logged and can be undone" and "agent
writes support dry-run and idempotency keys" must hold for write
endpoints nobody has written yet. Authentication comes in a later
task.

**Decision:** The API is mapped under one route group, `/api`, which
is where authentication will be attached. Every write endpoint is
mapped through `MapWrite`, which hands the request to `WriteRunner`.
The runner checks the query and body, sets `Source` (from
`ChangeSource`, the one place the auth task will change) and
`Description`, takes the write lock before anything is read, runs
the endpoint's work, saves, and then rolls back (dry run) or stores
the idempotency record and commits. A test lists the app's
endpoints and fails if a write endpoint lacks the runner's mark or
is missing from the catalog of requests that the guarantee tests
send to every write endpoint.

**Alternatives considered:** An endpoint filter around ordinary
handlers (it cannot capture the response body for replays without
running the result twice). Each endpoint doing it itself (one
forgotten step is a silent gap).

**Consequences:** Handlers do not save or answer; they change
entities and return a `WriteOutcome`. A new write endpoint fails the
guard test until it is added to `WriteCatalog`.

---

## 2026-11-14 Every write returns its change; a dry run is a rollback

**Context:** An agent needs to know what a write did, to be able to
undo it, and to try a write without making it.

**Decision:** Every write answers `{dryRun, change, data}`. `change`
is the change log entry, in the same shape as
`GET /api/changes/{id}`, or null if nothing changed; `data` is the
resource after the write (the item for its due date, rule, contexts
and reminders; the undone change for an undo; null after a delete).
`?dryRun=true` runs the same code and saves, so the database's
constraints are checked, then rolls the transaction back: nothing is
kept, the answer is 200 (unless it replays a kept answer; see the
entry on idempotency keys), `change.id` is null, and ids of anything
created are not real. Creates answer 201 and send no `Location`, so
a replay can repeat the answer exactly.

**Alternatives considered:** Working out a dry run with
`ChangeRecorder.Capture` alone (skips the constraints, and an undo
writes SQL that it would not see). Marking dry runs with a header
only (easy to miss). Bodies of the resource alone (the change number
needed for undo would be in a header).

**Consequences:** Anything a write will do outside the database (the
push in M5) must happen after the commit, and be skipped for a dry
run.

---

## 2026-11-14 Idempotency keys are kept in a table of their own

**Context:** STATUS.md suggested a nullable unique `IdempotencyKey`
column on `Changes`. A replay has to return the first answer, and a
request that changes nothing has no change to hang a key on.

**Decision:** `IdempotencyRecords` holds the key, method, path, a
fingerprint, the status and body of the answer, the change it made
and when. It is written in the same transaction as the change. It is
not in the change log and undo never touches it, so a retry after
its change was undone is still a replay, never a second write. The
fingerprint is SHA-256 of the method, the path and the exact bytes
of the body; the query is left out, so a dry run and the real request
are the same request. The path is taken as routing reads it, in
lower case and without a trailing slash, so an id in upper case is
the same request; an id spelt another way (without hyphens, in
braces) still counts as a different request. The same key with another fingerprint is 422.
A request that fails stores nothing, so it can be put right and sent
again with its key. Keys are optional, compared exactly, and never
pruned. The auth task may make them mandatory for agent tokens.

**Alternatives considered:** The column on `Changes` (cannot hold
the answer, and misses writes that change nothing). Hashing the body
as canonical JSON (more code, for clients that reorder properties on
a retry; those get 422). Storing failed answers (a client could not
correct a request without a new key).

**Consequences:** Concurrent requests with the same key queue on the
write lock; the second is answered from the record. Records grow by
one row per keyed write.

---

## 2026-11-14 Completing a repeating item names its occurrence

**Context:** Two identical "complete" requests without a key would
advance a repeating item twice.

**Decision:** Completing a repeating item requires `occurrence`, the
day the rule placed the occurrence being completed on
(`repeat.occurrence`); any other day is 409 with
`currentOccurrence`, and none is 400. For an item that does not
repeat `occurrence` is optional, and sending one is 409, because the
client's view is out of date. There are no ETags: rows have no
version column, and one would be logged and put back by undo.

**Alternatives considered:** `If-Match` with ETags (a version column,
and its values going backwards on undo). Accepting a duplicate (the
fault Todoist had).

**Consequences:** A known limit: in after-completion mode, completing
exactly one interval early gives back the same occurrence, so a
duplicate without a key passes the check. The item ends up exactly
as after one completion and the second records no change (a test
pins this), but only the idempotency key fully protects against
duplicates.

---

## 2026-11-14 Requests are read strictly

**Context:** An input that is silently ignored is how an agent
wrecks data while believing it did something else: `dry_run=true`
would be a real write, and a PATCH with a due date would leave the
date alone and report success.

**Decision:** A write takes no query parameter but `dryRun`, which
must be `true` or `false`. A body must be a JSON object sent as
`application/json`; a property that is unknown (property names are
case-sensitive), repeated, null where it cannot be, or of the wrong
type is a 400 naming it. A PATCH that names the due date, the rule,
contexts, reminders or completion is told which endpoint to use. In
a PATCH a property left out is kept and null clears it where that
means something. Reads refuse unknown query parameters too. Enum
names are accepted in any case, which is how System.Text.Json reads
them; numbers are refused.

**Alternatives considered:** The framework's lenient defaults
(case-insensitive names, numbers in strings, unknown properties
ignored).

**Consequences:** Clients must send exactly what the OpenAPI
document describes; a test ties the documented PATCH fields to what
the handlers apply.

---

## 2026-11-14 JSON, errors and the OpenAPI document

**Context:** The agent, the web UI and the phone all read the same
API.

**Decision:** camelCase names; enums as camelCase strings
(`nextAction`, `fixedSchedule`); instants in UTC with a `Z`, and an
offset required on input; dates `2026-11-20`; times `14:30:00`; a
relative reminder as `offsetMinutes`; the rule as RRULE text in
canonical form. Change log entries in responses keep the stored form
(PascalCase column names, upper-case ids) so they read like the
tables. Errors are problem details (RFC 9457): 400 with `errors` per
field, 404 for a missing or deleted thing, 409 for what the data as
it stands refuses (domain rules, a refused undo with `reasons`, a
context name in use, a stale occurrence), 413, 415, and 422 for a
reused key. The OpenAPI document is generated by ASP.NET Core and
served at `/openapi/v1.json` in every environment, with the XML
comments as descriptions.

**Alternatives considered:** A `TimeSpan` string or ISO 8601
duration for offsets (harder to write and to read correctly when
negative). Lower-case ids in the change log (would differ from the
tables and from the log's stored text).

**Consequences:** Ids appear in lower case in resources and upper
case in change log entries; compare them without regard to case.

---

## 2026-11-14 An undo can run inside a transaction it is given

**Context:** A dry run of an undo, and an idempotent undo, need the
undo to happen inside the request's transaction. `UndoAsync` opens
its own and refuses to run inside another.

**Decision:** `UndoWithinAsync` does the same work inside the open
transaction, under a savepoint, so a refused undo leaves the
transaction as it found it and the caller decides whether to commit.
`UndoAsync` is unchanged and still refuses an open transaction.

**Alternatives considered:** A callback inside `UndoAsync` for the
caller's extra writes (harder to follow).

**Consequences:** Two ways to undo, sharing one body; the API uses
the second.

---

## 2026-11-14 Refusals from Nudge.Core are logged, because a bug looks the same

**Context:** `Nudge.Core` signals a broken rule with
`InvalidOperationException` ("already completed") or
`ArgumentException` (a blank title), and the API answers those with
409 and 400. A programming error inside Core (a null `.Value`, a
`Single()` on the wrong count) throws the same types, so it would be
answered as the client's mistake and never show up as a failure.

**Decision:** Core is not changed in this task. Every problem the API
makes from an exception (in `Domain.Call`, `Domain.Refused` and the
undo endpoint) carries that exception, and is logged at Warning with
it, the method, the path and the status, when it is turned into a
response. The request body is not logged. Problems the API finds
itself (an unknown property, a stale occurrence) have no exception
and are not logged.

**Alternatives considered:** Leaving it (a Core bug would be
invisible). Answering every `InvalidOperationException` with 500
(every rule Core refuses would look like a crash).

**Consequences:** A Core bug shows in the log as a Warning, not as a
500, so it is found by reading the log, not by an alert. Follow-up,
which needs the owner's agreement because it changes Core and its
tests: give Core one exception type of its own for broken rules (for
example `RuleViolationException`), have the API map only that type,
and let everything else surface as a 500.

---

## 2026-11-14 API tokens are random, stored as hashes, made on the command line

**Context:** VISION.md asks for API tokens beside Google sign-in.
The clients are the planning agent (through the MCP server, M6), the
Android app (M5) and the web UI (M3). Google sign-in belongs to M3.

**Decision:** A token is `nudge_` followed by 32 bytes from
`RandomNumberGenerator` in base64url. Only its SHA-256 hash, in hex,
is stored, in the table `ApiTokens` (id, name, hash, created,
revoked), so a copy of the database or a backup gives no usable
token. A name is 1 to 64 letters, digits, `-`, `_` or `.`, is
unique ignoring the case of ASCII letters among all tokens, revoked
ones included, and cannot look like a GUID, because `revoke` takes a
name or an id. Tokens are made, listed and revoked only by a command
line on the same executable (`dotnet Nudge.Web.dll token create
<name>`, `token list`, `token revoke <name-or-id>`), which prints a
new token once on stdout and everything else on stderr. It reads the
app's configuration for the database, applies pending migrations as
startup does (only when some are pending, so on the running server
it leaves the migration lock alone), and needs no time zone. There
is no HTTP endpoint for tokens, so a token cannot mint another. No
expiry, no scopes, no record of last use, and no rate limiting:
256 random bits cannot be guessed, and recording use would make
every request a write.

**Alternatives considered:** Tokens managed over HTTP (a leaked
agent token could make itself a successor; that belongs behind
Google sign-in in M3). Storing the token encrypted (the key would
sit next to it). A slow hash such as PBKDF2 (only needed for
guessable secrets; it would cost every request). Names compared
exactly ("Agent" and "agent" would read as one in the log).

**Consequences:** A lost token cannot be shown again; revoke it and
make another, under a new name. After the first deploy the owner
makes the first token by hand on the server. Expiry, scopes and
last use are deferred; adding last use should not be a write per
request. The web UI may add token management behind sign-in in M3.

---

## 2026-11-14 Every endpoint needs a token unless it opts out

**Context:** An endpoint added later must not be open by mistake,
and a refusal should not help anyone probe for tokens.

**Decision:** A custom authentication scheme reads only
`Authorization: Bearer <token>` (the scheme name in any case; never
the query string or a cookie), hashes the token and looks up an
active one by hash. The principal carries the token's id and name.
The authorization fallback policy requires an authenticated user,
so every endpoint is protected unless it allows anonymous requests;
exactly two do: `/healthz` (Fly's health check) and
`/openapi/v1.json` (it describes a public repository's API). A path
that matches no endpoint is 401 too without a token. Every failure
(no header, another scheme, an empty, malformed, unknown or revoked
token) is the same 401 with `WWW-Authenticate: Bearer` and the same
problem details. Authentication runs before the write runner, so a
refused request reads no body and writes nothing, not even an
idempotency record. Neither a token nor the header is logged. The
OpenAPI document has an HTTP bearer scheme, and every operation that
is not anonymous requires it and documents the 401.

**Alternatives considered:** `RequireAuthorization()` on the `/api`
group (an endpoint mapped outside it would be open). Distinct
answers for unknown and revoked tokens (they tell an attacker what
exists). A 404 for unknown paths without a token (it maps the API
for anyone).

**Consequences:** Tests fail if any endpoint but those two becomes
anonymous, if any other endpoint answers without a token, if a
refused write changes anything, or if a token, its hash or the
header reaches the log. The web UI's sign-in (M3) will be a second
scheme next to this one.

---

## 2026-11-14 Tokens are security state; the change log names them

**Context:** The change log records every save and undo puts rows
back. A token row in it would put hashes into API responses, and an
undo of a revocation would bring a token back.

**Decision:** Like `IdempotencyRecords`, `ApiTokens` is left out of
the change log and out of undo: making or revoking a token records
no change, and an undo of an entry naming the table is refused.
Every change made through the API is recorded with the source
`api:<token name>`, set in `ChangeSource.For`, which refuses to name
a request that was not authenticated. Idempotency keys stay optional
and are not scoped per token: the request fingerprint does not
include the token, so two tokens sending the same key and request
get one result. The MCP server (M6) must always send a key.

**Alternatives considered:** Logging token rows with the hash left
out (undo would still revive a revoked token). Keys scoped per token
(one owner; a retry from a rotated token should still be a replay).
Keys mandatory for agent tokens (there are no kinds of token yet;
the MCP server is where the rule belongs).

**Consequences:** Who made and revoked tokens, and when, is in the
token table itself, not the log. Renaming a token is not offered,
because old changes would name a token that no longer has the name.

---

## 2026-11-15 The token command never creates or migrates a database

**Context:** The entry of 2026-11-14 on API tokens said the token
command applies pending migrations as startup does, so that it works
on a fresh database. A security review showed what that allows: run
without `ConnectionStrings__Nudge` (a `fly ssh console` session
without the image's environment, say) it created `nudge.db` where it
ran and minted a working-looking token into it, exit 0; and run
while the app was starting after a deploy, it could migrate beside
the app, after dropping the migration lock.

**Decision:** This replaces that part of the earlier entry. The
database must already exist and have no pending migrations; the
command never calls the migration or lock code. It opens the file
with SQLite's `Mode=ReadWrite`, which fails on a missing file, so
creating one is impossible rather than only checked. A missing file
is refused, exit 1, with the path looked at and
`ConnectionStrings__Nudge` named; a pending migration is refused,
saying to start the app or wait for it to finish starting. The
`Database: <file>` line stays first on stderr. Separately, the
request log category `Microsoft.AspNetCore.Hosting.Diagnostics`,
which writes each URL with its query string, is held at Warning in
code, so a token sent in the query cannot reach the log whatever
the settings file says.

**Alternatives considered:** Keeping the migration and only warning
about a new file (a warning on stderr is easy to miss in a script).
Creating the database but refusing to mint into an empty one (the
stray file would still be left behind).

**Consequences:** The first token can only be made after the app
has started once on the database. Tests make their databases the
way startup does before running the command.

---

## 2026-11-15 The alarm spike is a separate MAUI project, merged on its own

**Context:** M2 asks whether MAUI delivers exact alarms reliably on
the owner's phone. The M1 pull requests (#2 to #6) were still open,
and the spike must not touch the server's build or CI.

**Decision:** The spike is `spikes/AlarmSpike`, its own project and
not in `Nudge.sln`, on `m2-alarm-spike` cut from main (PR #7), so it
can be merged before or after the M1 stack. It is a real MAUI app
(`MauiApplication`, a MAUI page), Android only, because the question
is whether alarms arrive when the MAUI process must start cold for
the broadcast; the receivers themselves use only Android APIs and
two files, never a MAUI page or service. Android 8.0 (API 26) and
later, target API 35; Release builds a plain APK for arm64 and x64,
signed with the local debug key, to sideload. The repository's
`Directory.Build.props` applies, so warnings are errors there too;
the build has none. No network permission, and only invented test
text.

**Alternatives considered:** A plain .NET for Android app (lighter,
but it would not answer the MAUI question). Adding the project to
`Nudge.sln` (CI would then need the Android workload). Basing the
branch on the M1 stack (it could not be merged on its own).

**Consequences:** CI and the Docker image never see the spike, but
merging it to main still starts the gated deploy job. Nothing from
the spike is meant to survive into M5 as is.

---

## 2026-11-15 The alarm spike targets .NET 9

**Context:** The .NET 10 SDK on the development machine has the
MAUI and Android workload manifests but not their packs, so
`net10.0-android` does not build there, and installing workloads
was not allowed in that session. The .NET 9 SDK and its MAUI 9 and
Android 35 packs are installed.

**Decision:** `net9.0-android` with MAUI 9.0.51 (the workload's
version, restored from NuGet), and a `global.json` in the spike's
folder that selects the .NET 9 SDK (9.0.205, rolling forward within
9.0). The README says how to move to .NET 10 (`dotnet workload
install maui-android`, then change the target and `global.json`).

**Alternatives considered:** Installing the .NET 10 workloads (not
allowed without the owner). A `global.json` at the root (it would
change the server's SDK).

**Consequences:** .NET 9 is at or near the end of its support. That
is acceptable for a throwaway spike; M5 must use a supported
version. Alarm delivery is Android's work in either version; MAUI's
start-up cost, which the spike measures, may differ a little.

---

## 2026-11-15 Exact alarm permissions for a sideloaded app

**Context:** `setAlarmClock` needs exact-alarm permission on
Android 12 and later. Android 12 and 12L only know
`SCHEDULE_EXACT_ALARM`, which the user can revoke and which newer
versions deny by default; Android 13 added `USE_EXACT_ALARM`,
granted at install and not revocable, which Google Play allows only
for alarm and calendar apps.

**Decision:** Declare `USE_EXACT_ALARM`, and `SCHEDULE_EXACT_ALARM`
with `maxSdkVersion="32"`. Nudge is sideloaded, so Play's policy
does not apply, and a reminder app is the case `USE_EXACT_ALARM`
exists for. Also `POST_NOTIFICATIONS`, requested at first start on
Android 13 and later, and `RECEIVE_BOOT_COMPLETED`. The app never
asks to be exempt from battery optimisation, so the test shows
whether `setAlarmClock` gets through without it; the page shows
whether it is exempt.

**Alternatives considered:** `SCHEDULE_EXACT_ALARM` on every version
(revocable, and off by default on Android 14 for new installs).
Asking for the battery exemption (it would hide what the test is
about).

**Consequences:** If Nudge ever goes to the Play Store this needs
revisiting. M5 can copy these declarations.

---

## 2026-11-15 How the alarm spike behaves and what it logs

**Context:** The spike's log is the evidence for the M2 checklist,
and VISION says a swipe is not an answer.

**Decision:** Pending alarms are a tab-separated file in private
storage, rewritten atomically; the event log is an append-only text
file in the app's folder on shared storage, so `adb pull` reads it
from a Release build, and each line also goes to logcat. Every line
has local time with offset and milliseconds. Each process start is
logged with how long MAUI took to start; each fire with its delay
against the intended time (LATE over 10 s, the pass threshold) and
the Doze, screen and battery saver state. Notifications use one
high-importance channel, category reminder, not ongoing. Complete
removes the alarm; Snooze brings it back in 2 minutes; Schedule
opens the app through an activity intent (Android 12 and later
refuse an activity started from a receiver behind a notification)
and removes the alarm, as the spike has no time picker. A swipe or
"Clear all" (the delete intent) is logged and the alarm comes back
in 30 s, a test value. The boot receiver (`BOOT_COMPLETED`,
`MY_PACKAGE_REPLACED`, `QUICKBOOT_POWERON`) registers pending
alarms again, fires overdue ones at once as LATE and re-posts
unanswered notifications. The app is not direct-boot aware. Opening
the app re-registers nothing; a button does.

**Alternatives considered:** SQLite (a dependency for a few lines).
The log in private storage only (a Release build cannot be read
with `adb run-as`). Re-registering whenever the app opens (it would
hide whether the boot receiver worked).

**Consequences:** The re-notify interval in VISION's open questions
is still open; 30 s is only for testing. An alarm due between a
reboot and the first unlock fires late, at the unlock; M5 could use
direct-boot storage to avoid that.

---

## 2026-11-15 Testing Doze with alarm clocks

**Context:** On the emulator, `dumpsys deviceidle force-idle` was
refused while an alarm clock was due within the hour, and
scheduling one while forced ended deep Doze. Android does not enter
deep Doze when an alarm clock is due within `min_time_to_alarm`
(1 hour by default), and leaves Doze to deliver one.

**Decision:** The spike has a 3-hour button for a Doze test by hand
(screen off, phone still, overnight). The adb route in the README
first stops Play services from resetting device settings
(`device_config set_sync_disabled_for_tests until_reboot`), shortens
`min_time_to_alarm` to a minute, forces Doze, schedules, and puts
everything back afterwards. A pass is judged by the delay and by
`get deep` showing IDLE or IDLE_MAINTENANCE until the alarm, not by
the `doze:` value at delivery, which is usually "no".

**Alternatives considered:** A 60-minute alarm for the Doze step (it
can never meet deep Doze).

**Consequences:** On the emulator, with that setting shortened, an
alarm fired 3.8 s late through forced deep Doze with the process
dead. The real result must come from the phone.

---

## 2026-11-15 A finished item cannot be given a repeat rule

**Context:** CodeRabbit (PR #2) found that `Item.Repeat` on a
finished item installed the rule and new dates but kept
`CompletedAt`. The item then repeated on paper, could not be
completed, and its reminders stayed silent. The API reached this
through `PUT /api/items/{id}/repeat`.

**Decision:** `Repeat` throws `InvalidOperationException` on a
finished item, before changing anything, as `Complete` does. The API
answers it with 409, dry runs included. To make a finished item
repeat, undo its completion first.

**Alternatives considered:** Reopening the item in `Repeat` (a second
way to reopen, next to undo, which the 2026-11-13 decisions keep as
the only one; and a reopening that is a side effect of setting a
rule is easy to miss).

**Consequences:** A finished repeating series cannot be restarted with
a new rule without an undo. If the owner wants that, an explicit
reopen operation is the place for it.

---

## 2026-11-15 Project foreign keys use ClientNoAction, not Restrict

**Context:** CodeRabbit (PR #3) said that with `DeleteBehavior.Restrict`
on the optional `Items.ProjectId` and `Projects.ParentId`, EF Core
sets the key to null on tracked dependents when their project is
removed. A test confirmed it: the tracked item lost its project, and
the tracked child project its parent, in the change tracker. The
save was still refused, by the change log's rule that project rows
are never removed, so the database was not touched.

**Decision:** Both relationships use `DeleteBehavior.ClientNoAction`:
EF leaves tracked dependents alone and the database refuses the
delete. Migration `KeepTrackedDependentsOnProjectRemoval` changes the
foreign keys from `ON DELETE RESTRICT` to SQLite's default,
`NO ACTION`, which also refuses a delete of a row still referenced
(the foreign keys are not deferred). SQLite cannot alter a foreign
key, so the migration rebuilds `Items` and `Projects`.

**Alternatives considered:** Keeping `Restrict` and relying on the
change log's rule (the tracked objects are still changed, and a
caller that catches the refusal and saves again would save them).
Turning off cascade timing for the whole context (it would change
other relationships too).

**Consequences:** One more migration, which copies both tables when
it runs. A test upgrades a database with rows from the `ApiTokens`
migration and checks that they come through.

---

## 2026-11-15 The Android app is written in Kotlin, offline-first

**Context:** The plan was a .NET MAUI Blazor Hybrid app sharing
Razor components with the web UI, with the M2 spike to show whether
MAUI could deliver exact alarms. The owner wants the most native and
fastest app possible, and does not want a .NET runtime and a WebView
on the phone. The owner also wants offline use to be seamless, as in
Todoist: no signal needed, sync in the background.

**Decision:** The Android app is Kotlin with Jetpack Compose. It
keeps a local database, applies changes locally and syncs in the
background. The recurrence engine is written a second time in
Kotlin. The server stays the authority: offline changes are queued
and replayed with idempotency keys, and the server's answer replaces
the phone's. Recurrence test cases are kept as a data file that both
engines run, the two named regression tests among them. The owner
made this decision on 2026-11-15, before the MAUI spike was run on
the phone.

**Alternatives considered:** MAUI Blazor Hybrid (a WebView UI on the
.NET runtime: slower start, larger app, the least native). MAUI with
native controls (one recurrence engine, but still the .NET runtime
the owner objected to). A Kotlin thin client that asks the server
for everything (no second engine, but no offline use).

**Consequences:** "One language" in VISION.md is replaced. There is
no shared UI library; the web UI's components are its own. The
recurrence rules exist twice and can drift, which the shared test
cases and the server's authority are there to contain. The server
needs an endpoint for changes since a sync point before M5. M2
becomes a Kotlin alarm spike; the MAUI spike (PR #7) was closed
without merging, and its DECISIONS.md entries of this date apply
only to that closed branch. A second toolchain (Android Studio,
Gradle) is needed, in CI too.

---

## 2026-11-15 The Kotlin alarm spike uses platform APIs only

**Context:** M2 has to show that a native Kotlin app delivers exact
alarms on the owner's phone before the real app (M5) is built. The
MAUI spike (PR #7) was closed without merging, so its behaviour,
permissions and log format, recorded in its DECISIONS.md entries of
this date, would otherwise apply to nothing on main.

**Decision:** The spike is a standalone Gradle project in
`spikes/AlarmSpikeKotlin`, not part of `Nudge.sln`; the server's
build, tests and CI never see it. It uses only the Android platform
APIs (`Activity`, `Notification.Builder`,
`AlarmManager.setAlarmClock`, views built in code): no Compose, no
AndroidX, no other libraries, so what it shows is about Android
itself. It is built with Gradle 8.11.1, the Android Gradle Plugin
8.7.3 and Kotlin 2.0.21; the Gradle wrapper is committed with the
distribution's SHA-256 pinned. It keeps the MAUI spike's application
id `nudge.spike.alarms`, and carries over its behaviour, permissions
(`USE_EXACT_ALARM`, `SCHEDULE_EXACT_ALARM` up to API 32, boot and
notification permissions), constants and log format, which this
entry now records for main. The event log falls back to the app's
private storage while shared storage is not mounted, moves those
lines over later, and never throws, so logging cannot crash a
receiver.

**Alternatives considered:** Compose and AndroidX, as M5 will use
(closer to the real app, but more start-up code and dependencies
between the alarm and the measurement). Building the spike inside
the future app's project (nothing to reuse yet; it would tie the
throwaway code to M5's structure).

**Consequences:** M5 will add Compose and AndroidX, so its start-up
can be slower than the spike's; the spike's cold-start numbers are a
lower bound. The spike needs the Android SDK and a JDK 17 to build,
outside CI. Installing it over the MAUI build needs an uninstall
first, as the two are signed with different keys.

---
