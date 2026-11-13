# Status

**Last updated:** 2026-11-12
**Current milestone:** M1 Core, recurrence, REST API

## In Progress
- Nothing. M0 is complete.

## Recently Completed
- Deployed to Fly.io as `nudge-gtd` (region `ord`), one machine,
  SQLite on volume `nudge_data`, Litestream replicating to the
  Tigris bucket `nudge-gtd-backup`.
- Retargeted to .NET 10; public repo `kpanko/nudge`; CI green.

## Next Up
1. Rehearse a restore from the Litestream backup.
2. Start M1 with the recurrence engine and its tests.

## Blockers / Open Questions
- None.

## Key Context
- The working folder is still named `tbd-project-name`.
- MAUI and Android workloads are installed for .NET 9; the .NET 10
  workloads have not been checked.
- `dotnet workload list` hangs on this machine; avoid it.
- Pushes to main deploy after approval of the `production`
  environment in GitHub Actions. The local
  flyctl is old and failed to allocate IPs on first deploy; they
  were allocated by hand.
- A restore from backup has not been rehearsed yet.
