## Slice 7 — Scale Validation and Closeout Handoff
Status: drafted on 2026-10-12
You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Preserve all existing modified and untracked work. Do not reset, stash, discard, or stage unrelated files.
- Do not modify `.env*`, local agent configuration, credentials, or the untracked assignment PDF.
- Start after the full Compose stack and feature slices are integrated.
- Report only measurements actually obtained; do not label the system high-volume-ready by inference.

Task: Validate the integrated system, collect a reproducible performance baseline, and close documentation gaps without inventing service-level targets.

Goal:
Provide credible correctness, resilience, and measured performance evidence for the assignment while making unresolved production targets explicit.

Important context:
- The job description says “high volume,” but no traffic profile, QPS, p95/p99 latency, availability, or retention target has been supplied.
- The system uses PostgreSQL, Redis, Kafka, Entra, API, Analytics worker, and Blazor Server. Local tests and container runs do not equal production acceptance.
- This is a validation/documentation slice; do not add speculative architecture to make a benchmark look better.

In scope only:
1. Run the full solution build and tests plus real integration tests against the local Compose services.
2. Add a repeatable load/performance script for anonymous guest creation/redirect, public managed-link redirect, and, where safely configurable, authenticated management/analytics calls.
3. Capture throughput, latency percentiles, error rate, cache hit ratio, database resource/connection observations, Kafka consumer lag, and test conditions.
4. Exercise bounded dependency failure/recovery scenarios (database, Redis, Kafka) where the local environment allows.
5. Reconcile README/architecture/setup docs and record a concise final implementation-versus-verification status.

Out of scope:
- Setting numeric pass/fail SLOs without operator-supplied targets.
- Production deployment, real tenant app registration, destructive production tests, or testing against real user links.
- Load beyond local hardware/container capacity or claims extrapolated from it.
- Refactoring unrelated source or deleting existing tests/artifacts.

Required discovery before edits:
- Read `AGENTS.md`, the final architecture/README, all slice implementation notes, and the final Compose configuration.
- Inspect existing scripts and test commands to reuse project conventions.
- Confirm the test environment and local host capacity before choosing a bounded load profile.
- Confirm no real credentials/tenant or user data will be captured by benchmark logs.
- Review the full dirty diff and identify exact files within this closeout scope.

Implementation requirements:
- Make load tests bounded, opt-in, reproducible, and clearly scoped to local test data.
- Never run destructive or high-volume tests against a real service without explicit authorization.
- Do not hard-code authentication secrets; accept test credentials/configuration via local environment only and never echo them.
- Record environment, versions, concurrency, duration, warm/cold cache state, and whether auth/broker/database were real.
- Verify a guest link stops redirecting at seven days and has no click analytics; do not simulate passage of time by changing production data.
- Report observations without claiming production readiness unless thresholds and environment are supplied and met.
- Document missing values needed to convert the baseline into acceptance gates: traffic mix, sustained and burst RPS, latency objective, availability, analytics freshness, retention, recovery, and deployment environment.

Exception / observability requirements:
- Ensure benchmark and failure-test outputs are sanitized; do not record tokens, cookies, full URLs with sensitive query parameters, or provider payloads.
- Failure scenarios must record observed errors and recovery behavior without suppressing exceptions or presenting degraded responses as success.
- Correlate relevant API and worker evidence by request/event identifiers where available.

Realtime / no-polling requirements:
- Not applicable: validation scripts are bounded test clients, not operator-facing runtime workflows. Do not add a polling fallback or timer-based UI refresh.

Testing:
- Run `dotnet build SchwabUrlShortener.slnx` and `dotnet test SchwabUrlShortener.slnx --nologo`.
- Run targeted real-service tests and the bounded load/failure checks against the local Compose stack.
- Verify the handoff docs and project setup instructions agree with the implementation.
- State skipped/blocked checks exactly; test doubles and successful builds do not count as real-service verification.

Documentation:
- Create a dated validation/closeout note with commands, measured results, environment, real boundaries exercised, known issues, and unverified production requirements.
- Update README and architecture docs surgically; preserve the assignment framing and avoid unsupported capacity claims.

Completion criteria:
- [ ] Full solution build and tests are recorded.
- [ ] A bounded, reproducible load baseline reports conditions and measured outcomes.
- [ ] Guest-link expiry and no-analytics behavior are validated.
- [ ] Failure/recovery checks distinguish real-service evidence from test doubles.
- [ ] No numeric SLO or high-volume readiness claim is invented.
- [ ] Documentation lists unverified production requirements and exact next decisions.
