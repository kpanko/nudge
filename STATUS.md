# Status

**Last updated:** 2026-11-12
**Current milestone:** M0 Foundation

## In Progress
- M0: solution, health endpoint, CI workflow and Dockerfile exist
  locally. Not yet pushed or deployed.

## Recently Completed
- Name, architecture, language and milestone plan agreed.

## Next Up
1. Create the GitHub repository and push; confirm CI is green.
2. Fly.io: app, volume, Litestream, deploy job with approval gate.
3. Start M1 with the recurrence engine and its tests.

## Blockers / Open Questions
- Fly CLI is installed as `flyctl` but not logged in.
- The solution targets net10.0 but only the .NET 8 and 9 SDKs
  are installed locally; install the .NET 10 SDK to build here.

## Key Context
- The working folder is still named `tbd-project-name`.
- MAUI and Android workloads are already installed.
- `dotnet workload list` hangs on this machine; avoid it.
