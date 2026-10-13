# Slice 4 canonical integration plan — 2026-10-13 04:50 CDT

## Purpose

Integrate the reviewed Redis public-redirect cache from `codex/slice-4-redis-redirect-cache` into the canonical `codex/integrate-slices-1-3` checkout while preserving local data, credentials, unrelated work, and the Slice 4 source worktree.

## Scope and starting state

- Canonical checkout: `/Users/michaelinman/Repos/SchwabUrlShortener`, clean at `088946d7bc7b6a2bf318cfc939dc59bbef5daf64` before this plan.
- Slice 4 checkout: `/Users/michaelinman/Repos/SchwabUrlShortener-slice4`, clean at `1909c87bb1efa0ab8ee13e5ca566d6d0b8fb28c8`.
- Review found the Slice 4 branch descended from the canonical tip, with intended source, test, README, and dated-document changes; `git diff --check` passed.
- After the timeout cleanup, the guarded real PostgreSQL/Redis integration project passed 13/13 tests against `Schwab_test_local` and isolated loopback Redis port 63652. The earlier 11-test report predates the two connection lifecycle tests. The build, 24 unit tests, and 17 Web tests were reported passing after the fix and must be distinguished from checks run after integration.

## Steps

1. Save this plan in the canonical `docs/` directory and verify its exact content and worktree status.
2. Integrate the committed Slice 4 branch without deleting or resetting either worktree.
3. Build the integrated solution and run unit, Web, and guarded real PostgreSQL/Redis integration tests. Only the disposable `Schwab_test_local` database may be used; do not display, replace, or save credentials.
4. Record the integrated results and remaining live/deployment gaps in this document. Review intended staged paths and commit the integration record.

## Acceptance criteria

- Canonical history includes Slice 4 commits and the integration plan; no unrelated changes or secrets are committed.
- Integrated solution builds, and its test results are recorded with exact counts and environment.
- Application database `Schwab_local`, local passwords, API keys, Docker volumes/containers, and source worktrees are preserved.
- No deployment or operational acceptance is claimed. Live wrong-scope rejection, second-tenant isolation, actual seven-day guest expiry, and post-merge live Redis behavior remain separately identified until observed.

## Outcome

- The canonical branch merged Slice 4 without conflicts at `cf083e2c8f2709a3c5a1f49d2e4f73e48fa2c34c`; parents are `088946d7bc7b6a2bf318cfc939dc59bbef5daf64` and `1909c87bb1efa0ab8ee13e5ca566d6d0b8fb28c8`. The source worktree remained clean at `1909c87`.
- Independently checked the merge ancestry, canonical and source worktree status, and `git diff --check`; only this intended integration plan was untracked after the merge and tests.
- User-reported local command output: `dotnet build` of the canonical solution succeeded in 9.0 seconds, with no warnings shown.
- User-reported local command output: guarded `dotnet test` of the canonical solution exited 0 with 24/24 unit, 13/13 real PostgreSQL/Redis integration, and 17/17 Web tests passing, zero failures or skips. The test process used only `Schwab_test_local` and isolated loopback Redis port 63652; the helper checked the database target and suppressed raw test output to avoid exposing credentials. This is local verification, not deployment.
- No application database, password, or API key was intentionally changed, and no container or volume was deleted or reconfigured. The integration suite clears rows only in the guarded disposable `Schwab_test_local` database and uses unique Redis test namespaces.
- There is no branch push or deployment. Live wrong-scope rejection, second-tenant isolation, actual seven-day guest expiry, and manually running the merged API against Redis remain unverified.
