# Access and sign-in

**Real Google sign-in is not proven here.** See "What this cannot
prove" in [../SKILL.md](../SKILL.md).

## Sub-features

- API tokens: create, use, list, revoke, from the command line only
- API refuses a missing, wrong or revoked token
- Web UI locked when sign-in is not configured
- Public endpoints: `/healthz`, `/openapi`
- Signed-in pages, on the test host only

## How to get to it

As the owner, on a server: `dotnet Nudge.Web.dll token create <name>`,
then `Authorization: Bearer <token>` on API calls.

In the browser: any page goes to `/signin`, then to Google. With sign-in
not configured, as on the launched app, it ends at
`/sign-in-unavailable` (503, heading "Sign-in is not configured").

Signed-in views (`nav.views`): Inbox `/`, Next `/next`, Projects
`/projects`, Waiting `/waiting`, Someday `/someday`, Review `/review`,
Missed `/missed`, Changes `/changes`.

## Driving it

### API tokens

```bash
NV_ANON=1 nv_api auth-none GET /api/items
nv_expect "auth: no token is 401" 401 "$NV_STATUS"
NV_TOKEN=nudge_invented-not-a-real-token nv_api auth-wrong GET /api/items
nv_expect "auth: wrong token is 401" 401 "$NV_STATUS"

SECOND=$(nv_cli token create verify-second 2> /dev/null | tr -d '\r')
nv_expect "token: command printed a token" nudge_ "${SECOND:0:6}"
NV_TOKEN=$SECOND nv_api auth-second GET /api/items
nv_expect "token: new token works" 200 "$NV_STATUS"
nv_expect "token: list shows it" 1 \
    "$(nv_cli token list 2> /dev/null | grep -c verify-second)"
nv_cli token revoke verify-second > /dev/null 2>&1
nv_expect "token: revoke succeeded" 0 "$?"
NV_TOKEN=$SECOND nv_api auth-revoked GET /api/items
nv_expect "token: revoked token is 401" 401 "$NV_STATUS"
nv_sql auth-row "SELECT RevokedAt IS NOT NULL FROM ApiTokens WHERE Name = ?" \
    verify-second
nv_expect "token: row is marked revoked" 1 "$NV_ROWS"
nv_sql auth-hash "SELECT count(*) FROM ApiTokens
    WHERE Name = ? AND length(Hash) > 0 AND Hash NOT LIKE 'nudge_%'" \
    verify-second
nv_expect "token: stored, and not in the clear" 1 "$NV_ROWS"
SECOND=
```

### Web UI lock and public endpoints

```bash
NV_ANON=1 nv_api ui-home GET /
nv_expect "ui: page redirects when signed out" 302 "$NV_STATUS"
nv_expect "ui: to sign-in" "$NV_BASE/signin?returnUrl=%2F" "$NV_LOCATION"
NV_ANON=1 nv_api ui-signin GET "$NV_LOCATION"
nv_expect "ui: sign-in redirects" 302 "$NV_STATUS"
nv_expect "ui: to unavailable" /sign-in-unavailable "$NV_LOCATION"
NV_ANON=1 nv_api ui-unavailable GET "$NV_LOCATION"
nv_expect "ui: unavailable is 503" 503 "$NV_STATUS"
nv_expect "ui: says why" 1 \
    "$(grep -c '<h1>Sign-in is not configured</h1>' "$NV_BODY")"

nv_api ui-token GET /inbox
nv_expect "ui: an API token does not open a page" 302 "$NV_STATUS"

NV_ANON=1 nv_api public-health GET /healthz
nv_expect "public: health" 200 "$NV_STATUS"
NV_ANON=1 nv_api public-openapi GET /openapi/v1.json
nv_expect "public: OpenAPI document" 200 "$NV_STATUS"
nv_unverified "real Google sign-in" \
    "needs the owner on the deployed app"
```

### Signed-in pages (test host, fake Google)

```bash
nv_ui_smoke
```

This runs `OwnersDay_InTheBrowser` in Chromium: capture, process, add a
context, complete, undo, open every view. It has its own host and
database. It counts only if the log shows tests passed, with none
failed or skipped. Zero tests, or no browser, is a FAIL.

Install the browser once, as CI does. This needs PowerShell (`pwsh`),
which a fresh Ubuntu VM may not have, and `--with-deps` installs the
system libraries Chromium needs there:

```text
dotnet build --configuration Release
pwsh tests/Nudge.Web.UiTests/bin/Release/net10.0/playwright.ps1 \
    install --with-deps chromium
```

On Windows, leave out `--with-deps`.

## End state

- `ApiTokens` has this run's token and `verify-second` with `RevokedAt`
  set. Only hashes are stored.
- A signed-out client reached only the redirects and the 503 page.

## Gotchas

- The token is shown once, on stdout. Never echo it or save it to
  evidence: `nv_api` sends it by curl config on stdin.
- Do not set `Nudge__Google__*` to reach the sign-in flow: that makes
  the app contact the real Google.
- `/test-only/fake-google/auth` exists only in the test host. A 404 or
  a sign-in redirect for it on the launched app is correct.
- `nv_ui_smoke` proves the pages, not the sign-in.
