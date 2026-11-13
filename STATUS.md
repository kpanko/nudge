# Status

**Last updated:** 2026-11-12
**Current milestone:** M1 Core, recurrence, REST API

## In Progress
- M1. Recurrence engine is done (PR open); entities are next.

## Recently Completed
- Recurrence engine in `Nudge.Core`: `RecurrenceRule` (RRULE
  subset), `Recurrence` (fixed-schedule and after-completion),
  `LocalTimeResolver` (DST), with unit and CsCheck property tests.
- Deployed to Fly.io as `nudge-gtd` (region `ord`), one machine,
  SQLite on volume `nudge_data`, Litestream replicating to the
  Tigris bucket `nudge-gtd-backup`.

## Next Up
1. Rehearse a restore from the Litestream backup.
2. M1 entities: item, project (nested), context, reminder. An item
   must keep the rule's occurrence day apart from its due date.
3. The named regression test "setting a due date keeps the repeat
   rule" needs the item entity; "complete once advances once"
   exists at engine level and should be repeated at item level.

## Blockers / Open Questions
- None.

## Key Context
- MAUI and Android workloads are installed for .NET 9; the .NET 10
  workloads have not been checked.
- `dotnet workload list` hangs on this machine; avoid it.
- Pushes to main deploy after approval of the `production`
  environment in GitHub Actions. The local
  flyctl is old and failed to allocate IPs on first deploy; they
  were allocated by hand.
- A restore from backup has not been rehearsed yet.
