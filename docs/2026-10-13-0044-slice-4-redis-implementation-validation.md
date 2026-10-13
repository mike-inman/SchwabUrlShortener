# Slice 4 Redis redirect cache implementation and validation — 2026-10-13 00:44 CDT

## Purpose and scope

Implement `docs/2026-10-13-0036-slice-4-redis-redirect-cache-handoff.md` on dedicated worktree `/Users/michaelinman/Repos/SchwabUrlShortener-slice4`, branch `codex/slice-4-redis-redirect-cache`, from exact commit `088946d7bc7b6a2bf318cfc939dc59bbef5daf64`. The dated plan is `2026-10-13-0040-slice-4-redis-redirect-cache-plan.md`. The canonical integration checkout and Slice 1–3 worktrees were not edited.

## Implementation decisions

- `RedirectTarget` contains only immutable fields needed for redirect and durable managed-hit identification: mapping ID, short code, destination, link mode, tenant ID, and guest expiry. The cache is not used for tenant-scoped management, authorization, or analytics responses.
- Redis keys use `shortener:redirect:v1:<lowercase ASCII code>` by default. Short codes are validated and trimmed; the optional `RedirectCache:Namespace` is constrained to ASCII letters, digits, colons, and hyphens. PostgreSQL `citext` uniqueness and Redis lowercase keys agree on case-insensitive codes. No connection credential, URL, or token appears in keys or logs.
- Cache population happens only after successful PostgreSQL insertion or a PostgreSQL lookup. Duplicate and failed insert paths do not publish a different redirect. A repeated explicit managed code returns its existing mapping; blank optional codes can still create separate links.
- Cache entries expire within one hour. Guest TTL is capped to the remaining absolute seven-day lifetime; the service checks expiry again on every hit. No negative results are cached. Redis misses, invalid values, timeouts, and outages fall back to PostgreSQL. Managed redirects always execute a tenant- and ID-constrained durable PostgreSQL hit update, including on a cache hit; its failure remains an HTTP failure. Guest redirects never write analytics.
- Redis operations have a 750 ms wait bound, and connection attempts use a 500 ms client connect timeout. `/health/ready` now checks both PostgreSQL and Redis; Redis outage yields 503 while guest redirects can still fall back to PostgreSQL. Structured cache hit, miss, read/write error, and PostgreSQL fallback logs carry correlation IDs where available and omit values and credentials.
- Configuration names are `ConnectionStrings:Redis` (`ConnectionStrings__Redis` in the environment) and optional `RedirectCache:Namespace` (`RedirectCache__Namespace`). No credential file or secret store was edited.

## Local checks and real-service evidence

- `dotnet build src/Shortener.Api/Shortener.Api.csproj --nologo`: passed, zero warnings/errors after initial implementation.
- `dotnet test tests/Shortener.UnitTests/Shortener.UnitTests.csproj --nologo`: passed, 23/23 before the final focused unit case.
- Focused real Redis/PostgreSQL integration tests: 2/2 passed against a newly created isolated `redis:7-alpine` container `schwab-slice4-redis` on loopback port 63652 and the guarded PostgreSQL 16 database `Schwab_test_local` on loopback port 63651. The tests observed a real Redis key and bounded TTL, a cache-hit guest redirect with PostgreSQL deliberately unavailable, a key deletion followed by PostgreSQL miss fallback and cache repopulation, guest expiry, Redis-unavailable fallback, and degraded readiness. A warm managed cache entry with PostgreSQL unavailable returned 500 instead of hiding the durable analytics failure.
- `dotnet build SchwabUrlShortener.slnx --nologo`: passed, zero warnings/errors.
- `dotnet test SchwabUrlShortener.slnx --nologo`: passed, 24 unit, 11 real PostgreSQL/API integration, and 17 Web tests; zero failures/skips. The connection string was assembled only in the test process from the existing restricted local API secret store with the database field set to `Schwab_test_local`. No saved secret or application database setting was changed or printed. The test factory guards that exact database name before truncating mappings. Redis test factories use unique namespaces rather than clearing Redis data.

## Remaining verification and handoff

The source path has been built and tested locally; no branch push, merge, deployment, production Redis, production readiness, or scale result is claimed. Live Entra wrong-scope rejection, second-tenant isolation, and guest expiry at the actual seven-day boundary remain unverified. Redis Cluster, persistent cache recovery, eviction behavior under load, and production outage behavior were not exercised. Review this branch and its test evidence before integrating Slice 4 or starting Slice 5.
