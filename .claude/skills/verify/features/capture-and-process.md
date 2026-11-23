# Capture and process

## Sub-features

- Capture a title to the inbox
- Process it to a list: next action, waiting for, someday
- Complete an item that does not repeat
- Dry run (`?dryRun=true`) and `Idempotency-Key`

## How to get to it

As the owner, in the browser: open **Inbox** (`/`), type in **Capture**,
press **Add to inbox**. On the row choose a **List** and press
**Process**. Then **Next** (`/next`) and **Complete**.

Selectors: `nav.views`, `input[name=title]`, `li.item`,
`select[name=list]`, `button.complete`, `.notice`, `.empty`.

As an API client: `POST /api/items`, `PATCH /api/items/{id}`,
`POST /api/items/{id}/complete`.

## Driving it

```bash
T="Invented capture of a plumber call"
nv_api capture POST /api/items "{\"title\":\"$T\"}"
nv_expect "capture: created" 201 "$NV_STATUS"
nv_expect "capture: lands in the inbox" inbox "$(nv_get data.list)"
nv_capture ITEM data.id
nv_sql capture-row "SELECT State FROM Items WHERE upper(Id) = upper(?)" "$ITEM"
nv_expect "capture: row is Inbox" Inbox "$NV_ROWS"

nv_api process PATCH "/api/items/$ITEM" '{"list":"nextAction"}'
nv_expect "process: accepted" 200 "$NV_STATUS"
nv_api process-read GET "/api/items?list=nextAction"
nv_expect "process: read answered" 200 "$NV_STATUS"
nv_expect "process: on next actions" yes "$(nv_has_id "$ITEM")"
nv_sql process-row "SELECT State FROM Items WHERE upper(Id) = upper(?)" "$ITEM"
nv_expect "process: row is NextAction" NextAction "$NV_ROWS"

nv_api complete POST "/api/items/$ITEM/complete"
nv_expect "complete: accepted" 200 "$NV_STATUS"
nv_expect_match "complete: completedAt is a time" \
    '^[0-9]{4}-[0-9]{2}-[0-9]{2}T' "$(nv_get data.completedAt)"
nv_api complete-again POST "/api/items/$ITEM/complete"
nv_expect "complete: a second time is refused" 409 "$NV_STATUS"
nv_api complete-list GET "/api/items?list=nextAction"
nv_expect "complete: list answered" 200 "$NV_STATUS"
nv_expect "complete: gone from open items" no "$(nv_has_id "$ITEM")"
nv_sql complete-row \
    "SELECT CompletedAt IS NOT NULL FROM Items WHERE upper(Id) = upper(?)" \
    "$ITEM"
nv_expect "complete: row has CompletedAt" 1 "$NV_ROWS"

D="Invented dry run item"
nv_api dry-run POST "/api/items?dryRun=true" "{\"title\":\"$D\"}"
nv_expect "dry run: answers 200" 200 "$NV_STATUS"
nv_expect "dry run: says so" true "$(nv_get dryRun)"
nv_capture DRY_ID data.id
nv_sql dry-run-row "SELECT count(*) FROM Items
    WHERE upper(Id) = upper(?) OR Title = ?" "$DRY_ID" "$D"
nv_expect "dry run: saved nothing" 0 "$NV_ROWS"

K="Invented idempotent item"
NV_HEADERS=(-H "Idempotency-Key: verify-capture-1")
nv_api idem-1 POST /api/items "{\"title\":\"$K\"}"
nv_expect "idempotency: first creates" 201 "$NV_STATUS"
NV_HEADERS=(-H "Idempotency-Key: verify-capture-1")
nv_api idem-2 POST /api/items "{\"title\":\"$K\"}"
nv_expect "idempotency: replay answers the same" 201 "$NV_STATUS"
nv_expect "idempotency: replay is marked" 1 \
    "$(grep -ci '^Idempotent-Replayed: true' "$NV_SCRATCH/last.headers")"
nv_sql idem-row "SELECT count(*) FROM Items WHERE Title = ?" "$K"
nv_expect "idempotency: one row" 1 "$NV_ROWS"
```

## End state

- The captured item has `State = NextAction` and `CompletedAt` set, and
  is absent from `GET /api/items`.
- The dry-run title has no row. The idempotent title has exactly one.
- `Changes` has one row per real write, none for the dry run or replay.

## Gotchas

- `POST /complete` with no body is right for an item that does not
  repeat. Naming an `occurrence` on one is a 409.
- `PATCH` refuses `due`, `repeat` and others with a 400 that says which
  endpoint to use. That is intended.
- The same `Idempotency-Key` with a different body is a 422.
- The web forms need HTTPS: antiforgery cookies are Secure-only, so
  plain `http://` form posts cannot work even when signed in.
