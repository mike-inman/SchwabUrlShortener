# URL Shortener Production-Minded Slices and Codex Handoffs

Status: drafted on 2026-10-12

## Purpose

Replace the earlier in-memory-first execution order with a slice plan that honors the selected production-minded stack while keeping the assignment deliverable reviewable. The corresponding gated handoffs are in this directory and can be sent to Codex one slice at a time.

This is a planning artifact only. It does not change application code or the pre-existing uncommitted files.

## Direction and decisions

- Runtime stack: .NET 10, ASP.NET Core API, Blazor Server, PostgreSQL, Redis, Kafka, Entra ID, and Docker Compose.
- PostgreSQL is the durable source of truth. An in-memory repository may be used only as a test double, not as the runtime implementation.
- Support two link modes: anonymous guest links expire seven days after creation and provide redirect only; signed-in managed links persist, belong to an Entra tenant, and provide management and analytics.
- Entra protects managed-link creation/management and analytics. Guest-link creation and all public short-link redirects remain anonymous.
- A managed link's tenant identity comes only from validated authentication claims (the Entra tenant ID); never accept a caller-supplied tenant ID as authorization. Guest links are explicitly unowned and cannot be managed or queried for analytics.
- Short codes are globally unique so a public redirect can resolve without an authenticated tenant context. Analytics and management operations are tenant-scoped and apply only to managed links.
- Bitly-inspired scope is intentionally limited to guest-versus-account link behavior, persistent account link lists, and account analytics. QR codes, branded domains, geographic/device/referrer analytics, app integrations, and subscription-tier limits are not part of the assignment plan unless the assignment explicitly requires them.
- Redis is a derived redirect lookup cache, not the source of truth.
- Kafka carries click analytics asynchronously. Use a durable delivery design and idempotent processing; do not let analytics failure silently become a successful analytics claim.
- Blazor Server uses the stable authenticated API contract rather than coupling UI components to infrastructure implementations.
- “High volume” is a qualitative design requirement from the job description, not a numeric capacity target. No QPS, p95/p99 latency, retention, or availability target has been supplied. The plan measures a baseline and leaves pass/fail SLOs unset until those targets are agreed.

## Current repository facts and gaps

- The solution has Domain, Application, Infrastructure, API, Analytics worker, Web, unit-test, and integration-test projects.
- Domain/application/service code and basic API routes have been added, but the API currently registers `InMemoryUrlShortenerRepository`.
- Infrastructure already references Npgsql, but there is no PostgreSQL repository or migration workflow yet.
- The API has create, resolve, and analytics routes; its current resolve API returns JSON and its generated short URL uses a hard-coded host.
- The Web project is still the generated Blazor Server starter UI. The Analytics worker is still a template worker.
- Unit tests include domain checks. The integration test refers to an older route and request shape and must be reconciled against the chosen API contract.
- Guest expiration is seven days from creation. Expired links must no longer redirect; physical retention/cleanup after expiry remains an operational policy to document, not an arbitrary hard-coded retention period.
- The checkout has pre-existing modified and untracked files. They are operator-owned baseline. Do not clean, stash, reset, discard, or stage them as a convenience. The assignment PDF and all environment/credential files must remain local and unmodified.

## Dependency map and concurrency

| Slice | Handoff | Depends on | Parallel guidance |
|---|---|---|---|
| 1 | `2026-10-12-1953-slice-1-entra-tenant-boundary-handoff.md` | Existing working tree | Run first; it establishes guest/managed link modes, tenant rules, and API contracts. |
| 2 | `2026-10-12-1953-slice-2-postgresql-persistence-handoff.md` | Slice 1 | Can run alongside Slice 3 after Slice 1 is integrated, because it owns Infrastructure/database files. |
| 3 | `2026-10-12-1953-slice-3-blazor-operator-ui-handoff.md` | Slice 1 API/auth contract | Can run alongside Slice 2 in a separate worktree; it owns Web files and consumes the HTTP API. |
| 4 | `2026-10-12-1953-slice-4-redis-redirect-cache-handoff.md` | Slices 1 and 2 | Run after Slice 2; shares runtime registration paths with later infrastructure work. |
| 5 | `2026-10-12-1953-slice-5-kafka-analytics-handoff.md` | Slices 1, 2, and 4 | Run after Slice 4 to avoid conflicting Infrastructure/API wiring. |
| 6 | `2026-10-12-1953-slice-6-compose-observability-handoff.md` | Slices 2–5 | Integrates the completed services; run after feature slices. |
| 7 | `2026-10-12-1953-slice-7-scale-validation-handoff.md` | Slice 6 | Final measured validation and documentation closeout. |

The only recommended implementation parallelism is Slice 2 (PostgreSQL) with Slice 3 (Blazor UI), after Slice 1 is integrated and the API contract is stable. Use distinct Codex worktrees based on the same committed baseline; do not run concurrent Codex agents in the current dirty worktree. Because the baseline currently contains uncommitted/untracked work, first preserve and establish an intentional shared baseline without including `.env*`, local agent configuration, or the assignment PDF. If that cannot be done safely, execute the handoffs sequentially in the current worktree and preserve all existing changes.

Redis and Kafka both touch infrastructure and runtime composition. Keep them sequential even if separate worktrees seem possible; avoid avoidable schema, DI, and API merge conflicts.

## Handoff use

1. Review the handoff and exact baseline commit before sending it to Codex.
2. Send only a handoff whose prerequisites are integrated.
3. For each implementation, review the diff, run its narrow validation, and record implementation versus real-service verification separately.
4. After parallel slices, merge one branch at a time and rerun the solution checks on the integrated result.
5. Do not claim high-volume readiness from architecture choices or mocked tests. Slice 7 records observed measurements and unresolved target decisions.

Each handoff uses the repository’s required gated format and can be checked with:

```bash
bash scripts/check-codex-handoff.sh docs/<handoff-file>.md
```

## Outcome

- [x] Reframed the old in-memory implementation plan around the accepted production-minded stack.
- [x] Captured the Entra access behavior, tenant isolation, and seven-day guest-link policy.
- [x] Defined slice dependencies and a bounded, conflict-aware concurrency option.
- [x] Created gated Codex handoffs for the implementation slices.
- [x] Preserved all pre-existing modified and untracked files; only new planning/handoff documents are in scope.
