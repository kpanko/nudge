# Change log and undo

## Sub-features

- Every write is recorded, with who made it
- Undo of an edit, a completion and a deletion
- Undo refused when the data has moved on
- An undo is itself a change that can be undone

## How to get to it

As the owner, in the browser: after any write the `.notice` banner says
what was done and has **Undo**. **Changes** (`/changes`) lists recent
changes in `table.changes`, each with **Undo**.

As an API client: `GET /api/changes`, `GET /api/changes/{id}`,
`POST /api/changes/{id}/undo`.

## Driving it

```bash
nv_api undo-create POST /api/items \
    '{"title":"Invented undo subject","due":{"date":"2030-03-01"},
      "repeat":{"rule":"FREQ=WEEKLY;BYDAY=FR","mode":"fixedSchedule"}}'
nv_expect "undo: item created" 201 "$NV_STATUS"
nv_capture ITEM data.id
nv_expect "undo: change names the token" api:verify- \
    "$(nv_get change.source | cut -c1-11)"

nv_api undo-complete POST "/api/items/$ITEM/complete" \
    '{"occurrence":"2030-03-01"}'
nv_expect "undo: occurrence advanced" 2030-03-08 \
    "$(nv_get data.repeat.occurrence)"
nv_capture COMPLETION change.id
nv_api undo-log GET "/api/changes/$COMPLETION"
nv_expect "undo: completion is in the log" "Complete item" \
    "$(nv_get description)"

nv_api undo-it POST "/api/changes/$COMPLETION/undo"
nv_expect "undo: accepted" 200 "$NV_STATUS"
nv_expect "undo: new change points back" "$COMPLETION" \
    "$(nv_get change.undoes)"
nv_capture UNDO change.id
nv_api undo-read GET "/api/items/$ITEM"
nv_expect "undo: occurrence is back" 2030-03-01 "$(nv_get repeat.occurrence)"
nv_sql undo-row "SELECT DueDate, OccurrenceDate, CompletedAt
    FROM Items WHERE upper(Id) = upper(?)" "$ITEM"
nv_expect "undo: database row is back" "2030-03-01|2030-03-01|NULL" "$NV_ROWS"

nv_api undo-twice POST "/api/changes/$COMPLETION/undo"
nv_expect "undo: the same change twice is refused" 409 "$NV_STATUS"
nv_api undo-undo POST "/api/changes/$UNDO/undo"
nv_expect "undo: an undo can be undone" 200 "$NV_STATUS"
nv_api undo-redone GET "/api/items/$ITEM"
nv_expect "undo: occurrence forward again" 2030-03-08 \
    "$(nv_get repeat.occurrence)"

nv_api undo-edit PATCH "/api/items/$ITEM" '{"title":"Invented first title"}'
nv_capture EDIT change.id
nv_api undo-edit-2 PATCH "/api/items/$ITEM" '{"title":"Invented later title"}'
nv_api undo-stale POST "/api/changes/$EDIT/undo"
nv_expect "undo: refused when data moved on" 409 "$NV_STATUS"
nv_sql undo-stale-row "SELECT Title FROM Items WHERE upper(Id) = upper(?)" \
    "$ITEM"
nv_expect "undo: refusal changed nothing" "Invented later title" "$NV_ROWS"

nv_api undo-delete DELETE "/api/items/$ITEM"
nv_expect "undo: delete accepted" 200 "$NV_STATUS"
nv_capture DELETION change.id
nv_api undo-gone GET "/api/items/$ITEM"
nv_expect "undo: deleted item is 404" 404 "$NV_STATUS"
nv_sql undo-soft "SELECT DeletedAt IS NOT NULL FROM Items
    WHERE upper(Id) = upper(?)" "$ITEM"
nv_expect "undo: row kept, marked deleted" 1 "$NV_ROWS"
nv_api undo-restore POST "/api/changes/$DELETION/undo"
nv_expect "undo: restore accepted" 200 "$NV_STATUS"
nv_api undo-back GET "/api/items/$ITEM"
nv_expect "undo: item is back" 200 "$NV_STATUS"
```

## End state

- The item exists with `DeletedAt` NULL, title `Invented later title`,
  occurrence `2030-03-08`.
- `Changes` keeps every step, including undos with `Undoes` set. Nothing
  is removed from the log.

## Gotchas

- `POST .../undo` answers with `change` = the undo and `data` = the
  change that was undone.
- A 409 from undo is often the correct result. Read `reasons`.
- `source` is `api:<token name>` for API writes.
