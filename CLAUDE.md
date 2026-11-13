# Project Management

This project uses a structured session workflow. These files maintain state:

| File | Purpose | Stability |
|------|---------|-----------|
| `VISION.md` | Goals, non-goals, architecture, core principles | Rarely changes |
| `MILESTONES.md` | Major phases, tasks, acceptance criteria | Changes per phase |
| `STATUS.md` | In-progress work, next steps, blockers | Changes per session |
| `DECISIONS.md` | Architectural decision log | Append-only |
| `GITHUB.md` | Repo and project board config (optional) | Set once |

## Starting a Session

Run `/start-session` to orient yourself — it reads the project docs,
shows what's in progress, and walks you through the work loop.
It is optional. If you know what you want to work on, just start.

## Ending a Session

Run `/end-session` to save state. Skip it only if nothing changed.

## During a Session

- When a task is completed, check it off in MILESTONES.md immediately.
- When a significant design decision is made, add it to DECISIONS.md.
- When you hit a blocker, add it to STATUS.md under Blockers.
- Do not modify VISION.md without explicitly discussing it with the user.

## GitHub Integration (optional)

If `GITHUB.md` is configured:
- Run `/setup-github` once to create labels and a project board.
- `/kickoff` will automatically create GitHub issues for every planned task,
  track them through implementation, and open PRs that close them.
- Run `/create-issue` to file a task to the backlog without implementing it.

## Build & Test

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Nudge.Web
```

Warnings are errors. Run `dotnet test` before committing.

## Rules

- Never commit personal data: real task content, task IDs, reminder
  contents, tokens, or the allowlisted account. Tests use invented
  data only.
- The named regression tests in MILESTONES.md M1 (complete once
  advances once; setting a due date keeps the repeat rule) must
  never be weakened or removed.
