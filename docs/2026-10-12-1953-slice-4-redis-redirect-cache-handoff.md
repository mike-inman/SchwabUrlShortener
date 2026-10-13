## Slice 4 — Redis Redirect Cache Handoff
Status: drafted on 2026-10-12
You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Preserve all existing modified and untracked work. Do not reset, stash, discard, or stage unrelated files.
- Do not modify `.env*`, local agent configuration, credentials, or the untracked assignment PDF.
- Start only after the tenant-aware PostgreSQL path is integrated and tested.
- Keep PostgreSQL authoritative; Redis is disposable derived state.

Task: Add Redis caching for public short-code resolution without weakening PostgreSQL consistency or tenant controls.

Goal:
Reduce database reads on hot redirect lookups while preserving correct redirects during cache misses, expiry, invalidation, and Redis outages.

Important context:
- PostgreSQL is the source of truth and short codes are globally unique.
- Public redirects are anonymous; management and analytics remain tenant-scoped and Entra-protected.
- Guest links expire seven days after creation and do not collect analytics. Managed links remain tenant-owned and their analytics are captured.
- Cache values should contain only the information required to resolve a code. Do not use cache state as authorization evidence or analytics truth.

In scope only:
1. Add an explicit redirect-cache abstraction and Redis-backed implementation.
2. Integrate cache-aside lookup into the public resolution path, with bounded TTL and create-time population/invalidation that never outlives a guest link's absolute expiry.
3. Add configuration, health reporting, and observability for Redis connectivity/cache behavior.
4. Add focused tests for hit/miss/expiry/create consistency and fallback behavior.

Out of scope:
- Replacing PostgreSQL or caching all analytics/management results.
- Kafka click event publishing or background worker changes.
- Redis Cluster, multi-region failover, or numerical scale claims.
- Storing tenant authorization decisions in Redis.

Required discovery before edits:
- Inspect the integrated Slice 1–2 application, repository, API resolution path, DI composition, and configuration.
- Inspect Infrastructure and project package versions before selecting the Redis client.
- Identify all mapping-creation paths and the public redirect lookup path so cache invalidation cannot be skipped.
- Review current dirty diffs in files targeted by this slice.

Implementation requirements:
- Use a supported Redis client; do not add a local in-memory cache as a normal-runtime fallback.
- Treat cache values as derived from PostgreSQL and use a bounded TTL. A cache miss always falls back to PostgreSQL.
- For guest links, cap cache TTL at remaining lifetime; expiration must be enforced on cache hits, not just database lookups. Never refresh the seven-day expiry during redirect or caching.
- Redirect cache use must not record hits for guest links. Managed-link analytics remain on the durable analytics path.
- When Redis is unavailable, preserve redirect correctness via PostgreSQL and emit a structured warning/metric; do not hide a broad exception or report Redis healthy.
- Avoid stale cache entries on mapping creation; there is no update/delete API unless discovered in the current contract.
- Keep cache keys namespaced and encode short codes safely. Do not put secrets or unnecessary tenant information in keys.
- Use cancellation-aware asynchronous calls and avoid unbounded retry loops.

Exception / observability requirements:
- Record cache hit/miss/error and fallback events with structured sanitized logs/metrics.
- Preserve PostgreSQL and Redis error context in server logs without exposing connection strings to clients.
- Redis failure must be visible to readiness/health policy and must not silently select a fake cache.

Realtime / no-polling requirements:
- Not applicable: redirect lookup is synchronous and no operator-facing progress stream is introduced. Do not add polling.

Testing:
- Test behavior with a real Redis instance/container for integration coverage when available; isolated fakes may be used for unit tests only.
- Cover cache hit, miss, expiration, and outage fallback; prove returned destinations still agree with PostgreSQL.
- Run targeted API/Infrastructure tests and then solution build/tests.
- Report exactly whether a real Redis server was exercised.

Documentation:
- Add a dated implementation/validation note with cache key/TTL policy, configuration names, health behavior, tests, and real Redis status.
- Update README setup instructions surgically without removing prior assignment context.

Completion criteria:
- [ ] Public resolution uses Redis for hot reads and PostgreSQL remains authoritative.
- [ ] Cache misses, expiry, and Redis outage preserve correct redirects.
- [ ] Guest cache entries cannot outlive the seven-day link expiry or generate analytics.
- [ ] Cache behavior is observable and configuration contains no secret values.
- [ ] Tests cover cache behavior, including a real Redis integration test or an explicit environment blocker.
