# Slice 4 Redis redirect cache plan — 2026-10-13 00:40 CDT

## Purpose and scope

Implement the committed Slice 4 handoff on `codex/slice-4-redis-redirect-cache` in `/Users/michaelinman/Repos/SchwabUrlShortener-slice4`, starting from `088946d7bc7b6a2bf318cfc939dc59bbef5daf64`. Keep PostgreSQL authoritative and the other worktrees, credentials, application database, and Web behavior unchanged.

## Tasks

1. Add an Application redirect-cache contract and immutable redirect value. Add a Redis implementation in Infrastructure with normalized namespaced keys, bounded TTL, cancellation-aware operations, and sanitized failures.
2. Use cache-aside lookup for public redirects and populate after confirmed PostgreSQL creation. Preserve durable managed hit updates and exact guest expiry. Add Redis readiness reporting and cache hit/miss/error/fallback telemetry.
3. Add focused unit coverage and real Redis/PostgreSQL integration checks. Use only `Schwab_test_local` for tests that truncate mappings.
4. Update README setup and record observed results, gaps, and intended files in this document. Review the staged diff before committing.

## Acceptance criteria

- Hot redirects can use Redis, while misses and Redis failure read PostgreSQL and managed hits always update durably.
- Guest cache entries cannot extend the seven-day absolute expiry or collect analytics.
- Duplicate/failed creation does not overwrite or publish a different redirect.
- Redis readiness is unhealthy during outage while safe public redirects can still use PostgreSQL.
- Focused checks, solution build, and guarded full tests pass, with real-service evidence separated from unit tests.

## Outcome

Implemented and verified locally. See `2026-10-13-0044-slice-4-redis-implementation-validation.md` for the design, guarded test results, and remaining gaps.
