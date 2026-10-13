# Slice 4 Redis connection timeout fix — 2026-10-13 04:18 CDT

## Purpose

Close the Slice 4 review finding that a timed-out `ConnectAsync` wait could leave a later successful `ConnectionMultiplexer` without an owner or disposal path.

## Scope and acceptance

- Change only the Slice 4 Redis connection lifecycle and focused test support.
- Share one pending connection attempt across requests, retaining the 750 ms per-request wait bound.
- Retry after a completed failed attempt and dispose a successful late connection when the cache service shuts down.
- Run the focused test, solution build, and relevant tests without changing saved credentials or the application database.

## Outcome

`RedisRedirectCache` now retains a single pending `ConnectAsync` task across timed-out callers. A completed failed attempt can be retried. On service shutdown, a connection that completes later is disposed. The connection wait remains bounded for each caller, and the cache still falls back to PostgreSQL on failure.

Local verification on the Slice 4 worktree:

- Focused connection lifecycle tests: 2/2 passed. One used the real Redis test container on loopback port 63652 to verify late disposal; neither test used PostgreSQL or saved credentials.
- Solution build: passed with zero warnings and errors.
- Unit tests: 24/24 passed.
- Web tests: 17/17 passed.

The guarded PostgreSQL/API integration suite was not rerun after this fix because this shell has no test connection configuration. Its previous 11/11 result is recorded in `2026-10-13-0044-slice-4-redis-implementation-validation.md`; this note does not claim a new full-suite pass. No source worktree outside Slice 4, credential store, application database, or deployment was changed.
