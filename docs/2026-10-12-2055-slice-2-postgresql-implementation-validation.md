# Slice 2 PostgreSQL implementation and validation

Date: 2026-10-12 20:55 CDT
Purpose: Plan and record real PostgreSQL persistence implementation, migration, tests, and operational limits for Slice 2.

## Plan and scope

- Work only in `/Users/michaelinman/Repos/SchwabUrlShortener-slice2` on `codex/slice-2-postgresql-persistence` from shared baseline `805f2430189a40d4aa019368f1d80c46509fd71c`.
- Preserve the frozen Domain/Application contract, Slice 3 worktree, canonical checkout, secrets, agent configuration, and assignment PDF.
- Replace normal API repository registration with PostgreSQL only. Add a versioned, checked-in migration and a database readiness check; do not mutate schema at API startup.
- Use a disposable PostgreSQL 16 container on a separate port for repository and API integration tests. Verify guest/managed persistence, exact expiry, tenant isolation, global code uniqueness under races, cancellation, managed analytics, and persistence across host restarts.
- Run targeted Infrastructure/API tests, then solution build and tests. Stage and commit only Slice 2 owned paths and this handoff/note after reviewing the staged diff.

## Acceptance criteria

- PostgreSQL is the runtime system of record with no in-memory fallback.
- Database constraints enforce mode/tenant/expiry/analytics invariants and global case-insensitive short-code uniqueness.
- Managed hit updates are atomic and guest redirects cause no analytics write.
- The migration is repeatable through explicit tooling, not automatic production startup.
- A real PostgreSQL server is exercised by integration tests; failures are inspectable and do not expose connection strings or URL query data.

## Implementation and migration

- Added an EF Core/Npgsql repository and a `url_mappings` migration. API registration uses only PostgreSQL; a missing connection string fails startup, and database failure returns an HTTP 500 with a safe problem response. The in-memory repository remains in source for isolated Slice 1 unit tests only.
- `ConnectionStrings__ShortenerDb` is required for runtime and migration tooling. `AzureAd__ClientId` remains required by Slice 1. No connection string or credential is tracked.
- Apply or reapply migrations with `dotnet ef database update --project src/Shortener.Infrastructure/Shortener.Infrastructure.csproj --startup-project src/Shortener.Infrastructure/Shortener.Infrastructure.csproj --context ShortenerDbContext`. The migration role needs permission for `citext` and schema creation. API startup does not apply migrations.
- The database stores guest links without a tenant and enforces expiry exactly 168 hours after creation. A check constraint excludes guest analytics and invalid managed ownership. A unique `citext` index enforces case-insensitive global code uniqueness. Managed queries filter by tenant; a parameterized atomic update increments managed hit counts.
- `/health/live` checks process liveness. `/health/ready` checks database access and pending migrations and returns 503 when unavailable or unmigrated. This does not verify live Entra authentication.

## Real database evidence and checks

- Disposable `postgres:16-alpine` container `schwab-slice2-impl`, PostgreSQL 16.14, bound to loopback port 63650. The unrelated service on host port 5432 was untouched. Final tests used a fresh dedicated database named `schwab_slice2_test_final`; its password was supplied through process environment only and was not recorded.
- `dotnet ef database update` applied `20261013015738_InitialUrlMappings` to the dedicated database. A second run reported no pending migrations. `dotnet ef migrations has-pending-model-changes` reported no model changes. Direct database inspection found one migration history row, four mapping indexes, and the mode check constraint.
- `dotnet test tests/Shortener.IntegrationTests/Shortener.IntegrationTests.csproj`: 8 passed against the disposable PostgreSQL database. Tests cover exact guest expiry boundary, anonymous guest redirects without analytics, managed tenant isolation and analytics, persistence across host restart, case-insensitive cross-mode code race, concurrent managed hits, cancellation, rejection of incorrect or missing guest expiry by the database, and readiness/explicit failure when PostgreSQL is unavailable. Integration tests reject database names outside `schwab_slice2_test_*` before truncating test rows.
- `dotnet build SchwabUrlShortener.slnx --nologo`: succeeded, 0 warnings, 0 errors.
- `dotnet test SchwabUrlShortener.slnx --nologo`: 19 unit and 8 real-PostgreSQL integration tests passed.

## Remaining verification

- Live Entra token acquisition and tenant claim validation require an actual multi-tenant app registration, tenant, and access token; this test run uses the existing test-only authentication handler. No live Entra claim is made.
- Production database provisioning, credentials, migration execution, deployment, and operational load/reliability remain unverified. This local container establishes PostgreSQL behavior only.
