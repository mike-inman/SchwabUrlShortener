## Slice 5 — Kafka Click Analytics Handoff
Status: drafted on 2026-10-13
Drafted at 05:24 America/Chicago. Purpose: gated implementation of durable managed-click analytics after Slice 4 integration.
You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, `origin/main`, worktree list, and `git status --short --branch` before edits.
- Preserve all existing modified and untracked work. Do not reset, stash, discard, or stage unrelated files.
- Preserve `.env*`, local agent configuration, API keys, passwords, user-secrets stores, databases, containers, and the assignment PDF. Never print secret values or run `dotnet user-secrets list`.
- The clean canonical pre-handoff tip was `1bd907cd6a3bb43a00d473c12b06e52fa75ced84` on `codex/integrate-slices-1-3`. This handoff must be committed before Slice 5 starts. Create a dedicated `/Users/michaelinman/Repos/SchwabUrlShortener-slice5` worktree on `codex/slice-5-kafka-analytics` from the exact committed handoff tip, which will be newer than `1bd907c`. Verify its ancestry and clean state. Keep implementation changes out of the canonical and Slice 1–4 worktrees.
- Do not present a mock broker or in-memory queue as delivered Kafka integration.

Task: Move click analytics to a durable Kafka-backed asynchronous pipeline with idempotent processing.

Goal:
Keep redirect behavior low-coupled to aggregate analytics while ensuring events have a documented delivery/retry path and are not double-counted on replay.

Important context:
- Slices 1–4 are integrated in the canonical branch. The Slice 4 merge is `cf083e2c8f2709a3c5a1f49d2e4f73e48fa2c34c`, followed by integration and local-smoke records. On the merged checkout, the user reported a successful solution build and 54/54 passing local tests: 24 unit, 13 real PostgreSQL/Redis integration, and 17 Web. The user also showed a local merged-API readiness 200, guest create 201, redirect 302, and a populated Redis key. These are local results, not deployment or production acceptance; see `docs/2026-10-13-0450-slice-4-canonical-integration-plan.md` and `docs/2026-10-13-0522-slice-4-local-smoke-and-slice-5-readiness.md`.
- The Analytics project is currently a template worker; it does not consume events. Current managed-hit counts are updated synchronously through PostgreSQL during redirect resolution. Review this boundary before replacing or augmenting it; preserve correct count and failure behavior through the transition.
- PostgreSQL is authoritative for mappings. The chosen architecture includes Kafka for click events and analytics processing.
- Tenant ID must accompany every persisted record/event. Public redirects remain anonymous, so the event’s tenant comes from the resolved mapping, not the request.
- Guest links are unowned, expire after seven days, and must not produce click events or analytics. Only managed-link redirects enter the analytics pipeline.
- High volume has no numeric QPS or latency target yet; avoid promising measured capacity.
- Live Entra wrong-scope rejection, second-tenant isolation, and actual seven-day guest expiry remain unverified and are outside Slice 5 implementation acceptance.

In scope only:
1. Define a versioned managed-link click-event contract containing event ID, tenant ID, mapping/short-code identity, and UTC occurrence time; exclude unnecessary personal data.
2. Add durable event capture and Kafka publishing using an outbox or another explicitly justified reliable delivery mechanism.
3. Implement the real Analytics worker producer/consumer responsibilities and PostgreSQL aggregate update path.
4. Make consumer processing idempotent and retries/dead-letter/failure visibility explicit.
5. Add real Kafka/PostgreSQL integration tests and operational metrics for processing and lag.

Out of scope:
- UI changes, Entra policy changes, redirect-cache redesign, or Compose integration.
- Storing IP addresses, user agents, referrers, or other personal data unless separately approved and documented.
- A simulation-only worker, fake Kafka producer, or claims of exactly-once end-to-end processing.
- Numeric production capacity/SLO certification.

Required discovery before edits:
- Inspect current PostgreSQL schema/migration and repository transaction boundaries, including the `Schwab_test_local` integration-test guard. The application database `Schwab_local` must not be used for truncating tests.
- Inspect Slice 1 tenant/event context, Slice 2 persistence, Slice 4 cache behavior, and API resolve flow.
- Inspect `src/Shortener.Analytics/Program.cs`, `src/Shortener.Analytics/Worker.cs`, `src/Shortener.Infrastructure/ShortenerDbContext.cs`, `src/Shortener.Infrastructure/PostgresUrlShortenerRepository.cs`, `src/Shortener.Application/UrlShortenerService.cs`, `src/Shortener.Api/Program.cs`, project configuration, and existing test conventions.
- Confirm Kafka client/broker availability and current project package compatibility.
- Review dirty diffs in targeted files before editing.

Implementation requirements:
- Persist click intent durably before acknowledging it as captured; choose and document an outbox-based transaction if no stronger existing mechanism exists.
- Use Kafka with explicit acknowledgements, retry/backoff bounds, and a stable partition key. Do not call the design “exactly once” unless every boundary proves it.
- Include `tenant_id` in the event and enforce tenant-aware aggregate writes. The consumer must not trust tenant fields from an unauthenticated caller; event production reads them from the stored mapping.
- Make duplicate delivery safe with event-ID deduplication/unique constraints and transactionally coupled aggregate updates.
- Keep redirect success/error policy explicit if event capture is unavailable; no silent loss or false analytics success. Preserve redirect behavior only if the selected durable path records or surfaces the missing event reliably.
- Ensure failure queues/retries and poison-event outcomes are inspectable and avoid logging payloads containing sensitive data.

Exception / observability requirements:
- Carry event ID, tenant ID, mapping ID, and trace/correlation ID through producer and consumer logs where available.
- Emit structured events/metrics for captured, published, consumed, retried, deduplicated, failed, and lagging work.
- Preserve safe failure diagnostics; never log credentials, raw connection strings, tokens, or unbounded broker payloads.

Realtime / no-polling requirements:
- Not applicable: this slice is a backend event pipeline, not an operator-facing progress UI. Do not implement UI polling; Slice 3 should use ordinary API requests for its current synchronous screen.

Testing:
- Cover outbox transaction boundaries, publish retry, consumer restart/replay, duplicate event delivery, tenant isolation, and aggregate counts.
- Use an isolated real Kafka-compatible broker and guarded `Schwab_test_local` PostgreSQL for integration tests when available. Never delete or reset existing containers, volumes, or application data to obtain a test environment. Test doubles remain unit-test-only.
- Run Analytics and Infrastructure focused tests, then solution build/tests.
- Report actual broker/database verification separately from mocked unit coverage; state concrete blockers if containers/services are unavailable.

Documentation:
- Add a dated implementation/validation note describing the event schema/version, delivery guarantees and limitations, retry/dead-letter operations, configuration names, and test environment.
- Update architecture and operator setup documentation surgically, retaining existing content.

Completion criteria:
- [ ] Managed-link click events are captured through a durable, documented path and carry `tenant_id`.
- [ ] Guest-link redirects produce no click event or analytics record.
- [ ] Kafka publishing and Analytics processing use real integrations with bounded retries and inspectable failures.
- [ ] Duplicate/replayed messages do not double-count analytics.
- [ ] Tests cover failure/replay behavior and distinguish real-service evidence from test doubles.
- [ ] No exactly-once or scale claim exceeds verified behavior.
