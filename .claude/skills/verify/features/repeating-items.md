# Repeating items

Holds the two **M1 regression behaviours**. These journeys must never
be weakened or removed. They are the live counterparts of
`CompletingARepeatingItemOnce_AdvancesItExactlyOnce` and
`SettingADueDate_NeverRemovesTheRepeatRule` in
`tests/Nudge.Web.Tests/Api/AcceptanceTests.cs`.

## Sub-features

- Fixed-schedule and after-completion repeat rules (RRULE)
- Completing one occurrence, which advances the item
- Rescheduling one occurrence, which keeps the rule and reminders
- Reminders relative to the due date

## How to get to it

As the owner, in the browser: open an item (`/items/{id}`). Under
**Repeat**, fill **Rule (RRULE)** and **Mode**, press **Set the repeat
rule**. Under **Due**, press **Reschedule**. At the top, press
**Complete the occurrence of YYYY-MM-DD**.

Selectors: `input[name=rule]`, `select[name=mode]`, `input[name=date]`,
`input[name=time]`, `input[name=offsetMinutes]`, `button.complete`.

As an API client: `PUT /api/items/{id}/repeat`,
`PUT /api/items/{id}/due`, `POST /api/items/{id}/complete` with
`{"occurrence":"YYYY-MM-DD"}`, `POST /api/items/{id}/reminders`.

## Driving it

### M1: completing once advances once

```bash
R="FREQ=WEEKLY;BYDAY=FR"
nv_api m1a-create POST /api/items "{\"title\":\"Invented weekly chore A\",
  \"due\":{\"date\":\"2030-01-04\"},
  \"repeat\":{\"rule\":\"$R\",\"mode\":\"fixedSchedule\"}}"
nv_expect "M1 once: created" 201 "$NV_STATUS"
nv_capture ITEM data.id
nv_expect "M1 once: first occurrence" 2030-01-04 "$(nv_get data.repeat.occurrence)"

BODY='{"occurrence":"2030-01-04"}'
NV_HEADERS=(-H "Idempotency-Key: verify-m1-once")
nv_api m1a-complete POST "/api/items/$ITEM/complete" "$BODY"
nv_expect "M1 once: complete accepted" 200 "$NV_STATUS"
nv_expect "M1 once: due advanced one week" 2030-01-11 "$(nv_get data.due.date)"
nv_expect "M1 once: occurrence advanced" 2030-01-11 \
    "$(nv_get data.repeat.occurrence)"
nv_expect "M1 once: item stays open" null "$(nv_get data.completedAt)"

# The same request again, with its key: answered, not redone.
NV_HEADERS=(-H "Idempotency-Key: verify-m1-once")
nv_api m1a-replay POST "/api/items/$ITEM/complete" "$BODY"
nv_expect "M1 once: replay answers 200" 200 "$NV_STATUS"
nv_expect "M1 once: replay is marked" 1 \
    "$(grep -ci '^Idempotent-Replayed: true' "$NV_SCRATCH/last.headers")"

# The same request again, without a key: refused.
nv_api m1a-again POST "/api/items/$ITEM/complete" "$BODY"
nv_expect "M1 once: resend without key refused" 409 "$NV_STATUS"
nv_expect "M1 once: refusal names the current one" 2030-01-11 \
    "$(nv_get currentOccurrence)"

nv_api m1a-read GET "/api/items/$ITEM"
nv_expect "M1 once: fresh read still 2030-01-11" 2030-01-11 \
    "$(nv_get due.date)"
nv_sql m1a-row "SELECT DueDate, OccurrenceDate, RecurrenceRule, CompletedAt
    FROM Items WHERE upper(Id) = upper(?)" "$ITEM"
nv_expect "M1 once: database row" "2030-01-11|2030-01-11|$R|NULL" "$NV_ROWS"
nv_sql m1a-log "SELECT count(*) FROM Changes c
    JOIN ChangeEntries e ON e.ChangeId = c.Id
    WHERE c.Description = 'Complete item' AND e.TableName = 'Items'
    AND upper(e.RowKey) LIKE '%' || upper(?) || '%'" "$ITEM"
nv_expect "M1 once: exactly one completion in the log" 1 "$NV_ROWS"
```

### M1: setting a due date keeps the repeat rule

```bash
R="FREQ=WEEKLY;BYDAY=FR"
nv_api m1b-create POST /api/items "{\"title\":\"Invented weekly chore B\",
  \"due\":{\"date\":\"2030-01-04\"},
  \"repeat\":{\"rule\":\"$R\",\"mode\":\"fixedSchedule\"},
  \"reminders\":[{\"offsetMinutes\":540}]}"
nv_expect "M1 due: created" 201 "$NV_STATUS"
nv_capture ITEM data.id
nv_capture REMINDER data.reminders.0.id

nv_api m1b-move PUT "/api/items/$ITEM/due" \
    '{"date":"2030-01-15","time":"09:30:00"}'
nv_expect "M1 due: reschedule accepted" 200 "$NV_STATUS"
nv_expect "M1 due: date moved" 2030-01-15 "$(nv_get data.due.date)"
nv_expect "M1 due: time set" 09:30:00 "$(nv_get data.due.time)"
nv_expect "M1 due: rule kept" "$R" "$(nv_get data.repeat.rule)"
nv_expect "M1 due: mode kept" fixedSchedule "$(nv_get data.repeat.mode)"
nv_expect "M1 due: series start kept" 2030-01-04 "$(nv_get data.repeat.start)"
nv_expect "M1 due: occurrence kept" 2030-01-04 \
    "$(nv_get data.repeat.occurrence)"
nv_expect "M1 due: reminder kept" "$REMINDER" "$(nv_get data.reminders.0.id)"

nv_api m1b-clear DELETE "/api/items/$ITEM/due"
nv_expect "M1 due: clearing the date is refused" 409 "$NV_STATUS"
nv_api m1b-patch PATCH "/api/items/$ITEM" '{"repeat":null}'
nv_expect "M1 due: PATCH cannot drop the rule" 400 "$NV_STATUS"

nv_sql m1b-row "SELECT DueDate, DueTime, OccurrenceDate, RecurrenceRule,
    RecurrenceMode, RecurrenceAnchor
    FROM Items WHERE upper(Id) = upper(?)" "$ITEM"
nv_expect "M1 due: database row" \
    "2030-01-15|09:30:00|2030-01-04|$R|FixedSchedule|2030-01-04" "$NV_ROWS"
nv_sql m1b-reminders \
    "SELECT count(*) FROM Reminders WHERE upper(ItemId) = upper(?)" "$ITEM"
nv_expect "M1 due: reminder row kept" 1 "$NV_ROWS"

# Completing the moved occurrence goes on from the rule's day.
nv_api m1b-complete POST "/api/items/$ITEM/complete" \
    '{"occurrence":"2030-01-04"}'
nv_expect "M1 due: moved occurrence completes" 200 "$NV_STATUS"
nv_expect "M1 due: next follows the rule" 2030-01-11 \
    "$(nv_get data.repeat.occurrence)"
nv_expect "M1 due: rule still there" "$R" "$(nv_get data.repeat.rule)"
```

## End state

- Chore A: `DueDate` and `OccurrenceDate` are `2030-01-11`, rule intact,
  `CompletedAt` NULL, exactly one `Complete item` entry in the log.
- Chore B after the move: `DueDate` `2030-01-15`, `OccurrenceDate` still
  `2030-01-04`, rule, mode, anchor and reminder row unchanged.

## Gotchas

- A repeating item **requires** `occurrence` on complete: without it, a
  400. A stale one is a 409 with `currentOccurrence`. That refusal is
  what stops a double complete, so a 409 on resend is the pass.
- The due date and the occurrence differ after a reschedule. Complete by
  `repeat.occurrence`, not `due.date`.
- `afterCompletion` counts from today in the owner's zone, so its next
  date depends on the real clock. Compute the expected date from the
  run's date. Do not hard-code it.
- Use a future series. Dates here are fixed in 2030 so that results do
  not change from day to day.
