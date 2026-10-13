## Slice 4 — Redis Redirect Cache Handoff
Status: drafted on 2026-10-13
Drafted at 00:36 America/Chicago.
You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm physical path, branch, commit, `origin/main`, and existing work before edits.
- Preserve `.env` files, API keys, passwords, local agent configuration, the unpublished assignment PDF, existing PostgreSQL data, and unrelated changes. Never print or list secret values.
- Keep the assignment within the scope below. Use narrow validation after the first substantive edit.
- Create a dedicated Slice 4 worktree and branch from the committed `codex/integrate-slices-1-3` tip containing this handoff. Do not change the Slice 1–3 source worktrees or canonical integration checkout while implementing Slice 4. The pre-handoff integration tip is `53858aa`; verify the actual handoff commit and start from that exact tip.

Task: Add Redis as a disposable cache for anonymous short-code redirects while PostgreSQL remains the source of truth.

Goal:
Hot public redirect lookups can avoid a PostgreSQL mapping read, while misses, expiry, new-link creation, and Redis outages still produce the correct redirect or 404 and preserve managed analytics.

Important context:
- Slices 1–3 are integrated on `codex/integrate-slices-1-3`. The integrated solution previously passed 19 unit, 9 guarded real-PostgreSQL/API integration, and 11 Web tests at `bc7af79`. Later Web-only commits added duplicate feedback, live dashboard updates, and restart recovery; the latest full solution build passed with zero warnings/errors and the latest Web suite passed 17/17. A full solution test run has not been repeated after those Web-only commits.
- On the integrated checkout, the user observed home-tenant Entra sign-in, managed list/create/detail, a new generated code for an existing destination, explicit-code duplicate feedback, a working public redirect and redirect count, anonymous guest creation/redirect, and Web restart recovery. The last duplicate toast and restart checks were confirmed by the user in the browser. These are local observations, not deployment or production acceptance.
- `UrlShortenerService.ResolveAsync` currently reads a `UrlMapping` through `IUrlShortenerRepository.GetByShortCodeAsync`, rejects expired links, and calls durable `RecordManagedHitAsync` for managed redirects. `Program.cs` maps anonymous `GET /r/{shortCode}` to this service and currently has PostgreSQL readiness only. Short codes are globally unique; managed reads and analytics remain tenant-scoped and Entra-protected.
- Live wrong-scope rejection, second-tenant isolation, and the actual seven-day guest-expiry boundary remain unverified. Production database, deployment, tenant onboarding/allowlist, and operational acceptance remain open. Do not represent Slice 4 as closing those gaps.

In scope only:
1. Add a redirect-cache contract and Redis implementation in the Application/Infrastructure layers, plus the API registration and configuration needed to use it.
2. Integrate cache-aside resolution and post-commit cache population or invalidation for both guest and managed creation paths. Keep a database fallback for cache misses and Redis errors.
3. Add Redis health/readiness reporting and structured, sanitized cache hit/miss/error/fallback observability.
4. Add focused unit and real Redis/PostgreSQL integration coverage, a dated implementation/validation note under `docs/`, and a surgical README setup update.

Out of scope:
- Web UI, dashboard refresh behavior, Entra settings, API authorization policy, tenant ownership rules, client secret rotation, or token-cache persistence.
- Kafka click events, Analytics worker, Docker Compose stack integration, Redis Cluster, multi-region behavior, or unmeasured scale claims.
- Replacing PostgreSQL, caching management/analytics responses, caching authorization decisions, or caching negative lookups unless a separate reviewed consistency policy justifies it.
- Deleting or resetting any existing container, volume, local database, password, API key, or source worktree.

Required discovery before edits:
- Inspect `src/Shortener.Application/UrlShortenerService.cs`, `src/Shortener.Application/Interfaces/IUrlShortenerRepository.cs`, `src/Shortener.Infrastructure/PostgresUrlShortenerRepository.cs`, `src/Shortener.Domain/UrlMapping.cs`, `src/Shortener.Api/Program.cs`, the relevant project files, current configuration, and the existing unit/integration tests.
- Trace every creation and public resolve path before choosing cache population/invalidation points. Check the existing case-insensitive short-code semantics and the EF mapping so Redis keys agree with PostgreSQL uniqueness.
- Confirm real Redis availability and the test database guard before any integration test. `Schwab_test_local` is the only disposable test database; `Schwab_local` is the application database. Do not copy, print, or change saved connection-string values.
- Review the committed diff and current dirty state in the dedicated Slice 4 worktree before editing.

Implementation requirements:
- Use a supported Redis client and a distinct cache abstraction. Do not add an in-memory normal-runtime fallback or treat Redis as authoritative.
- Cache only immutable redirect fields needed to route and evaluate guest expiry. Use namespaced, safely encoded, case-normalized keys, a bounded TTL, and no secrets in keys/values/logs.
- Enforce guest absolute expiry on cache hits and cap its Redis TTL to the remaining lifetime. Never extend a guest link's seven-day lifetime through cache activity.
- Populate/invalidate only after PostgreSQL confirms creation. A failed insert must not create a usable cache entry; a repeated explicit managed code must preserve its existing mapping. A blank optional code may create another link to the same destination.
- Keep managed hit counts and last-access time on the durable PostgreSQL path even when the mapping comes from Redis. Guest redirects must never record analytics. Adjust the repository/service contract if a cached immutable value cannot safely drive the current durable hit update.
- On Redis timeout or outage, resolve against PostgreSQL with cancellation-aware, bounded operations. Emit a structured warning/metric and make the Redis dependency unhealthy in readiness; do not expose connection strings or provider details to clients.
- Preserve anonymous public redirects, delegated `access_as_user` enforcement, and tenant-scoped management. Do not infer tenant ownership from Redis data.

Exception / observability requirements:
- Record hit, miss, error, and PostgreSQL fallback with structured sanitized logs or metrics and request correlation where available. Distinguish an absent code from cache unavailability. Do not swallow PostgreSQL failures behind a successful cache result when a durable managed hit must be recorded.

Realtime / no-polling requirements:
- Not applicable: public redirect lookup is a request/response operation and adds no operator UI. Do not add a Refresh control or polling to the Web app.

Testing:
- Run focused unit tests for hit/miss, key normalization, expiry, creation consistency, managed hit recording, and Redis outage fallback. Test doubles may verify branch logic only.
- Exercise a real Redis server/container alongside the guarded `Schwab_test_local` PostgreSQL database for integration coverage. Prove a cache hit, cache miss, guest expiry, and Redis-unavailable PostgreSQL fallback. Record an explicit environment blocker if real Redis cannot be tested; never claim mock coverage as real-service verification.
- Build the solution and run the relevant API/Infrastructure and guarded integration suites. Run the full solution suite when the required test database configuration is safely available. Preserve all application database rows and local credentials.

Documentation:
- Save a dated Slice 4 implementation/validation note under `docs/` describing key format, TTL/expiry policy, health behavior, configuration names, test results, real Redis status, and remaining gaps. Update README setup instructions without replacing prior context. Do not commit secret values.

Completion criteria:
- [ ] Public resolution can use Redis for hot mapping reads while PostgreSQL remains authoritative and managed hit updates stay durable.
- [ ] Misses, expiry, new-link creation, and Redis outage preserve correct redirects or 404 without stale or unauthorized results.
- [ ] Guest cache entries cannot outlive the seven-day absolute expiry or generate analytics.
- [ ] Redis dependency health and cache behavior are observable without leaking credentials.
- [ ] Focused tests pass, real Redis and guarded PostgreSQL results or a specific blocker are recorded, and the dedicated Slice 4 branch is reviewable with only intended files committed.
