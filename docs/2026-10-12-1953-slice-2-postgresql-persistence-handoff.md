## Slice 2 — PostgreSQL Persistence Handoff
Status: drafted on 2026-10-12
You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Preserve all existing modified and untracked work. Do not reset, stash, discard, or stage unrelated files.
- Do not modify `.env*`, local agent configuration, credentials, or the untracked assignment PDF.
- Build and test the real PostgreSQL path; an in-memory repository is not a runtime fallback.
- Start only after Slice 1’s tenant-aware application/repository contracts are integrated.

Task: Replace runtime in-memory URL mapping persistence with durable PostgreSQL storage.

Goal:
Persist guest and managed links safely across restarts, with seven-day guest expiry, managed tenant ownership, analytics boundaries, database-enforced uniqueness, and a repeatable schema migration path.

Important context:
- `src/Shortener.Infrastructure/Shortener.Infrastructure.csproj` already references Npgsql 9.0.3.
- `InMemoryUrlShortenerRepository` is currently the only repository and the API currently registers it directly.
- Slice 1 establishes guest/managed link modes, seven-day guest expiry, global short-code uniqueness, tenant-scoped management, and anonymous public resolution.
- The Analytics worker is not yet functional; do not implement Kafka/outbox in this slice.

In scope only:
1. Implement the PostgreSQL repository against the Slice 1 repository contract.
2. Add a versioned, repeatable database schema/migration approach for mappings, managed tenant ownership, guest expiry, and managed-link analytics.
3. Add configuration and dependency-injection registration for PostgreSQL without putting secrets in tracked files.
4. Enforce tenant-scoped managed-link access and globally unique short codes across guest and managed links in PostgreSQL constraints/indexes.
5. Ensure expired guest links cannot resolve as redirects and guest access never changes analytics fields.
6. Add real PostgreSQL integration coverage for both link modes, expiry, managed analytics reads, restart persistence, and uniqueness races.

Out of scope:
- Redis cache, Kafka events/outbox, or background analytics processing.
- Entra authentication changes or Blazor UI.
- Hard-coded connection strings, automatic production schema mutation at app startup, or an in-memory runtime fallback.
- Unrequested retention policies, multi-region replication, or capacity claims.

Required discovery before edits:
- Inspect the Slice 1 contracts and its validation outcome.
- Inspect `src/Shortener.Infrastructure/`, its project references, API DI registration, configuration, and current repository.
- Inspect both test projects and current package versions before selecting a migration/ORM strategy.
- Inspect `.gitignore` and existing environment configuration without printing or modifying secret values.
- Confirm the test environment can provide PostgreSQL; use the repository’s Compose stack if already available, otherwise document the exact external prerequisite instead of faking persistence.

Implementation requirements:
- PostgreSQL is the runtime system of record. Never select the in-memory repository through normal configuration or silently fall back on connection failure.
- Keep database access behind the Application repository contract and preserve tenant filtering on management/analytics queries.
- Enforce unique short codes in the database; handle concurrent conflicts as an explicit domain/API conflict rather than a success-shaped result.
- Persist guest links without a fabricated Entra tenant; managed records must retain their validated tenant ownership.
- Store guest expiration as an absolute UTC timestamp equal to creation time plus seven days. Expired guest links must not resolve; do not invent an additional physical-retention period or delete policy.
- Store/aggregate analytics only for managed links; guest redirects must not create click analytics or last-access updates.
- Use parameterized queries or a supported ORM. Choose one coherent, version-compatible migration strategy and check migrations into the repository.
- Make persistence operations cancellation-aware, asynchronous, and safe under concurrent requests. Do not overwrite analytics state with stale entity snapshots.
- Read the connection string from standard configuration/environment providers; use placeholders only in examples and never modify existing `.env*`.
- Ensure health/readiness can distinguish an unavailable database from a healthy application.

Exception / observability requirements:
- Use structured logging for database operations/failures without logging connection strings or URL query data unnecessarily.
- Preserve exceptions and add safe operation/correlation context; return actionable service errors without raw SQL/provider details.
- Connection failure is an explicit failure, not an in-memory fallback.

Realtime / no-polling requirements:
- Not applicable: this slice is synchronous storage and has no operator-facing progress workflow. Do not introduce polling.

Testing:
- Use a real PostgreSQL instance for repository integration tests (a disposable test container is appropriate if supported by the environment).
- Cover guest and managed link persistence, exact expiry behavior, tenant isolation, cross-mode duplicate-code races, cancellation, and managed analytics updates.
- Keep in-memory fakes confined to unit tests; label them as test doubles.
- Run targeted Infrastructure/API tests, then solution build and tests if the targeted checks are green.
- State clearly whether PostgreSQL tests ran against a real server/container or were blocked; mocked repositories do not prove persistence.

Documentation:
- Add a dated implementation/validation note describing migration commands, required configuration names (not values), real database evidence, and blockers.
- Update setup documentation surgically after verifying the existing text and retaining unrelated content.

Completion criteria:
- [ ] API runtime registration uses PostgreSQL and has no normal-path in-memory fallback.
- [ ] Managed-link tenant isolation and global code uniqueness across both modes are enforced by the database.
- [ ] Guest expiry is persisted and enforced without recording analytics or fabricating a tenant.
- [ ] A checked-in migration path creates and upgrades the schema repeatably.
- [ ] Real PostgreSQL tests cover persistence and concurrency behavior.
- [ ] No secret or local-only configuration value is added to tracked files.
