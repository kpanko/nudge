# Projects and contexts

## Sub-features

- Nested projects, and an item in a project
- Contexts with case-insensitive unique names
- Filtering items by project or context
- Deleting a project with everything under it, as one undoable change

## How to get to it

As the owner, in the browser: **Projects** (`/projects`), fill the new
project form. **Next** (`/next`) has **New context** and groups next
actions by context. An item's page (`/items/{id}`) has **Context** and
**Add context**.

Selectors: `input[name=name]`, `select[name=parentId]`,
`select[name=contextId]`, `section.group`, `li.item`.

As an API client: `/api/projects`, `/api/contexts`,
`PUT /api/items/{id}/contexts/{contextId}`,
`GET /api/items?contextId=...` or `?projectId=...`.

## Driving it

```bash
nv_api ctx-create POST /api/contexts '{"name":"invented-errands"}'
nv_expect "context: created" 201 "$NV_STATUS"
nv_capture CTX data.id
nv_api ctx-duplicate POST /api/contexts '{"name":"INVENTED-errands"}'
nv_expect "context: same name in other case refused" 409 "$NV_STATUS"
nv_sql ctx-count \
    "SELECT count(*) FROM Contexts WHERE lower(Name) = 'invented-errands'"
nv_expect "context: one row" 1 "$NV_ROWS"

nv_api prj-parent POST /api/projects '{"name":"Invented house"}'
nv_expect "project: parent created" 201 "$NV_STATUS"
nv_capture PARENT data.id
nv_api prj-child POST /api/projects \
    "{\"name\":\"Invented roof\",\"parentId\":\"$PARENT\"}"
nv_expect "project: child created" 201 "$NV_STATUS"
nv_expect "project: child is under parent" "$PARENT" "$(nv_get data.parentId)"
nv_capture CHILD data.id

nv_api prj-item POST /api/items "{\"title\":\"Invented gutter job\",
  \"list\":\"nextAction\",\"projectId\":\"$CHILD\"}"
nv_expect "project: item created" 201 "$NV_STATUS"
nv_capture ITEM data.id
nv_api ctx-add PUT "/api/items/$ITEM/contexts/$CTX"
nv_expect "context: added to item" 200 "$NV_STATUS"
nv_api ctx-filter GET "/api/items?contextId=$CTX"
nv_expect "context: filter finds the item" "$ITEM" "$(nv_get 0.id)"
nv_api prj-filter GET "/api/items?projectId=$CHILD"
nv_expect "project: filter finds the item" "$ITEM" "$(nv_get 0.id)"
nv_sql ctx-link "SELECT count(*) FROM ItemContexts
    WHERE upper(ItemId) = upper(?) AND upper(ContextId) = upper(?)" \
    "$ITEM" "$CTX"
nv_expect "context: link row" 1 "$NV_ROWS"

nv_api prj-delete DELETE "/api/projects/$PARENT"
nv_expect "project: delete accepted" 200 "$NV_STATUS"
nv_capture DELETION change.id
nv_api prj-gone GET "/api/items/$ITEM"
nv_expect "project: item under it is gone" 404 "$NV_STATUS"
nv_sql prj-soft "SELECT count(*) FROM Projects
    WHERE upper(Id) IN (upper(?), upper(?)) AND DeletedAt IS NOT NULL" \
    "$PARENT" "$CHILD"
nv_expect "project: both rows soft-deleted" 2 "$NV_ROWS"

nv_api prj-undo POST "/api/changes/$DELETION/undo"
nv_expect "project: one undo accepted" 200 "$NV_STATUS"
nv_api prj-back GET "/api/items/$ITEM"
nv_expect "project: item is back" 200 "$NV_STATUS"
nv_expect "project: item is in its project" "$CHILD" "$(nv_get projectId)"
nv_sql prj-restored "SELECT count(*) FROM Projects
    WHERE upper(Id) IN (upper(?), upper(?)) AND DeletedAt IS NULL" \
    "$PARENT" "$CHILD"
nv_expect "project: both rows restored" 2 "$NV_ROWS"
```

## End state

- One `Contexts` row, linked to the item in `ItemContexts`.
- Parent, child and item all present with `DeletedAt` NULL after the
  undo. The deletion and its undo are each one row in `Changes`.

## Gotchas

- `?projectId=` matches that project only, not projects under it.
- A deleted context stays hidden on its items and comes back on undo.
- `null` for `parentId` in a PATCH moves a project to the top level;
  leaving it out keeps the parent.
