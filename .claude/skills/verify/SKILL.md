---
name: verify
description: >-
  Prove a Nudge change works by driving the real ASP.NET Core app on a
  scratch SQLite database: the REST API, API-token auth, the token
  command line, the locked web UI, and the signed-in web UI through the
  Playwright test host. Use before calling a Nudge change done, when
  asked to verify or smoke-test it, or when tests alone would not show
  that the running app behaves.
---

# Verify Nudge in the real app

`dotnet test` proves the code. This skill proves the running app: a
published build, on a real port, with a real database file, driven
the way a client drives it. Everything is invented data on a scratch
database that is deleted afterwards. Only the evidence stays.

Never touch `/data`, the default `nudge.db`, Fly, production, real
tokens, Todoist or any real account.

## Launch

Use **bash**: Git Bash on Windows, bash on Linux. From PowerShell,
`bash` may be WSL, which is the wrong machine: use the Bash tool.

Run launch, doctor, drive and cleanup **in one shell command**, from
the repository root, with a timeout of at least 5 minutes. The app is
a background job of that shell, and an `EXIT` trap stops it if the
script dies.

```bash
. .claude/skills/verify/lib.sh
nv_launch && nv_doctor || { nv_stop; exit 1; }

# Drive: paste journeys from features/ here.

nv_stop
```

`nv_launch` does this, and refuses if any step is unsafe:

1. Finds a Python 3 with `sqlite3` by running it, so a broken shim is
   skipped. With none, it stops: database state would be unverified.
2. Resolves the evidence home (links, junctions, case, repeated
   slashes) and refuses the repository or `/data` before creating it.
3. Publishes `src/Nudge.Web` in Release to the run's own folder, under
   `env -i` with an allowlist that holds no application settings.
4. Refuses unless the connection string is exactly this run's
   `scratch/nudge.db`.
5. Starts `env -i <allowlist> dotnet Nudge.Web.dll > log 2>&1 &` as a
   direct job, in Production, with
   `ASPNETCORE_URLS=http://127.0.0.1:0` (the system picks a free port)
   and `Nudge__TimeZone=Etc/UTC`. Nothing else is inherited, including
   names bash cannot show, such as `ConnectionStrings:Nudge`.
6. Proves the database before writing to it: the server must have
   created the scratch file, and `token list` must name it.
7. Only then makes an API token, in the scratch database.

Any refusal is recorded as a FAIL with its error in the evidence, and
`nv_stop` and the shell then exit non-zero. If the evidence home itself
is refused, nothing is created, so the refusal is on stderr only.

After it, `$NV_BASE`, `$NV_PORT`, `$NV_PID` and `$NV_EVIDENCE` are set.

## Doctor

`nv_doctor` is read-only. **If it fails, do not drive**: results would
describe some other process or database. It checks that:

| Check | How |
| --- | --- |
| Right process | exactly one process has this run's dll on its command line, and it is the one started |
| Port is ours | the listener on `$NV_PORT` is that process |
| Right build | the source fingerprint (tracked and new files under `src`) and the dll hash match the launch |
| Right database | the connection string passes the guard, and the file has Nudge's application id |
| Server uses it | the server accepts a token that exists only in the scratch database, and refuses no token |

An edit under `src` after launch fails the build check. Launch again.

## Drive

Start from the feature that the change touches:
[features/README.md](features/README.md). Each journey is a block to
paste between doctor and `nv_stop`.

| Helper | Does |
| --- | --- |
| `nv_api LABEL METHOD PATH [JSON]` | Calls the app, saves evidence; sets `$NV_STATUS`, `$NV_LOCATION`, `$NV_BODY` |
| `NV_ANON=1 nv_api ...` | The same with no token |
| `NV_HEADERS=(-H "K: v")` | Adds headers to the next call only |
| `nv_get a.b.0.c` | One value from the last answer, or `<nv_get failed: ...>` |
| `nv_capture VAR a.b` | Keeps a value for later steps; empty, null or missing is a FAIL |
| `nv_has_id ID` | `yes` or `no`: whether the last answer, a list, holds that id |
| `nv_sql LABEL SQL [PARAM...]` | Read-only query of the scratch database; sets `$NV_ROWS`, columns joined by `\|` |
| `nv_cli ARGS...` | The app's command line (`token ...`) on the scratch database |
| `nv_expect LABEL EXPECTED ACTUAL` | Records PASS or FAIL; empty or failed reads never pass |
| `nv_expect_match LABEL REGEX ACTUAL` | The same, for a value known only by shape |
| `nv_unverified LABEL REASON` | Records something not exercised |
| `nv_ui_smoke` | Runs the signed-in browser test on the test host |

Proof standards:

- **Use the real user path**: the endpoint or page a client uses.
  Never write to the database to set up or to fake a result.
- **Capture the action and the resulting state**: the answer, then a
  fresh read, then the database row.
- **Verify side effects**: the change log entry, what must *not* have
  changed, and that a refused request changed nothing.
- **No empty comparisons.** Keep ids with `nv_capture`, never
  `X=$(nv_get ...)`, so that a failed call cannot compare equal to
  another failed call.
- **Label what was not exercised** with `nv_unverified`. Silence is
  not proof.
- **Never weaken a journey to make it pass.** Do not loosen an
  expected value, skip a step or retry until green. A failure is a
  finding: report it with its evidence. Do not fix product code as
  part of verifying unless that is the task.

## Evidence

Kept in `~/.nudge-verify/runs/<UTC time>-<shell pid>/evidence/`, outside
the repository (override the root with `NUDGE_VERIFY_HOME`; a path
inside the repository is refused).

| File | Holds |
| --- | --- |
| `summary.txt` | Counts of PASS, FAIL, UNVERIFIED, TEST-HOST |
| `results.tsv` | Every check: result, label, expected, actual |
| `NNN-label.txt` | Each request and answer, or query and rows, in order |
| `build.txt` | Commit, source fingerprint, dll hash, dotnet, OS |
| `app.log` | The app's own log |
| `ui-smoke-test-host.log` | The browser test, if it was run |

Tokens are never written there, and cleanup fails the run if anything
shaped like a token is found. Home folders are scrubbed from paths.
Use invented data only, so the evidence is safe to quote.

Report the counts from `summary.txt`, the evidence path, and every
FAIL and UNVERIFIED line.

## Cleanup

`nv_stop` runs on exit even after a failure. It stops the job, then
looks for what is left **by this run's dll path on the command line**,
because on Windows killing the shell's job can leave `dotnet.exe`
running. It force-stops only those PIDs and never kills by process
name. It then confirms that no such process remains and the port is
free, deletes the scratch database and build, and keeps the evidence.
Its exit status is non-zero if any check in the run failed, and a
shell that exits without calling it still exits non-zero.

If a shell died mid-run, stop what it left:

```text
. .claude/skills/verify/lib.sh
nv_recover ~/.nudge-verify/runs/<run>
```

A run folder that still has `state.env` was not cleaned up.

## What this cannot prove

Say these plainly in any report that touches them.

- **Real Google sign-in: not proven.** The launched app has no Google
  client, so its web UI is locked and every page leads to
  `/sign-in-unavailable`. `nv_ui_smoke` signs in through a fake Google
  at `/test-only/fake-google/auth`, which exists only inside the test
  host. It proves the pages work once signed in. It is recorded as
  TEST-HOST and is **not** evidence that real sign-in works, that the
  owner is admitted, or that another Google account is refused on the
  real provider. Only the owner, on the deployed app, can check that.
- **Signed-in UI on the launched app: not driven.** Do not add a
  bypass to reach it. A UI change needs `nv_ui_smoke`, or a new
  Playwright test in the change itself, and it covers only the steps
  that test performs.
- **Production deployment and Litestream restore: not proven.** Nothing
  here runs the Docker image, Fly, the `/data` volume, Litestream
  replication or a restore. A change to `Dockerfile`, `fly.toml`,
  `litestream.yml`, `run.sh` or migrations of real data needs its own
  check on the deployment, by the owner.
- **Android: not proven, and this skill never needs a phone.** The
  owner's phone is their main phone and is connected only on special
  occasions, so do not depend on it or ask for it mid-task. Nothing
  here shows that an alarm fires, survives Doze or a reboot, or that a
  notification action works. If a change affects alarms, notifications,
  reminder delivery, or what the phone app reads from the API, add a
  **"needs real phone"** checklist item to the PR description and
  `STATUS.md`, saying exactly what to check, and report it as unverified.
  An API journey proves only the server's side of that contract. This
  skill makes no claim about Android emulators.
- **Time.** The app uses the real clock, so journeys use fixed future
  dates. DST and month-end behaviour are proven by the property tests,
  not here.
