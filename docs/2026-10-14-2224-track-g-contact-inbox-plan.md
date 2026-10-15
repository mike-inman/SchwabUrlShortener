# Track G contact inbox plan — 2026-10-14 22:24 CDT

## Purpose

Give a configured customer admin a signed-in inbox for stored contact submissions, independent of notification delivery.

## Scope and boundaries

- Worktree: `/Users/michaelinman/Repos/SchwabUrlShortener-contact-inbox`, branch `codex/contact-inbox`, based on `origin/main` at `5f92bff`.
- Add exact issuer and subject admin configuration, protected API reads and idempotent handling, nullable migration, safe Web rendering, Redis hint updates, tests, Bicep parameter, and operator runbook.
- Preserve concurrent Track C email and Track H UI changes. Do not deploy Bicep or alter production identity without Michael's what-if review and approval.

## Tasks

1. Implement fail-closed admin authorization using the validated `iss` and `sub` claims and existing delegated scope policy.
2. Add keyset inbox and handled endpoints; publish admin-scoped hints after inserts and handled updates.
3. Add the nullable database column and generated EF migration; test migration forward and backward on a disposable schema.
4. Add token-backed Web client, admin-only header count and page with local time, notification status, safe text rendering, pagination, and debounced live updates.
5. Add API and bUnit coverage, run a zero-warning build and full isolated Compose suite, then review Bicep and document operator steps.

## Acceptance

- Unauthenticated and non-admin callers cannot read or mutate messages; responses exclude the client key hash.
- Newest-first pagination is stable under inserts; handled is idempotent; open Web views update through Redis hints without polling.
- Full solution tests pass in a fresh disposable project. The PR stays separate from production approval and live sign-in acceptance.

## Result

2026-10-15: Implemented the admin policy, API inbox and handled endpoints, nullable migration, Web inbox and header count, Redis hints, Bicep parameter, and runbook. `dotnet build SchwabUrlShortener.slnx --no-restore -v quiet` passed with zero warnings. The isolated Compose test suite passed: 42 integration, 44 unit, and 88 Web tests. The integration suite applied and reversed the migration on a disposable schema. `az bicep build --file infra/p2-apps.bicep --stdout` passed with four existing linter warnings outside this slice. Production admin identity confirmation, what-if approval, deployment, and signed-in acceptance remain open.
