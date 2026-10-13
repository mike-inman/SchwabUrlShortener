# ADR: Link Insights — Phase 1 (Collection)

Date: 2026-10-13 15:00 America/Chicago
Branch: `claude/link-insights-collection` (worktree `/Users/michaelinman/Repos/SchwabUrlShortener-insights`), based on `codex/workspace-authorization` @ `e7e24560cb9f90e39d1d549f15931b55b05dea05`.
Scope: Phase 1 only, per `docs/2026-10-13-1410-link-insights-handoff.md` and its "Amendment — 2026-10-13 14:30 CDT". No UI, no Insights API endpoints — those are Phase 2.

## Context

Real click data must start accumulating as soon as possible (operator decision in the amendment). This ADR fixes the decisions needed to do that safely while a second agent (Codex) actively edits `codex/workspace-authorization` in a separate worktree on the same shared Postgres/Kafka/Redis stack. Per the task's shared-environment rules, this work must not restart or rebuild any container, must not migrate `Schwab_test_local` or `Schwab_local`, and must not touch Web auth/token code.

## Decision 1 — Event versioning: `ManagedClickEventV3` extends the V2 (workspace) lineage only

- `ManagedClickEventV1` (legacy, `TenantId`-keyed) is untouched and keeps being produced by the legacy tenant-owned capture path (`CaptureManagedClickAsync`).
- `ManagedClickEventV3` is added as a new record alongside `ManagedClickEventV2`, carrying the V2 fields (`EventId, WorkspaceId, MappingId, ShortCode, OccurredAtUtc, TraceId`) plus `VisitorHash, ReferrerGroup, ReferrerHost, DeviceClass, OsFamily, BrowserFamily, Language, IsBot`. Only the workspace-native capture path (`CaptureWorkspaceClickAsync`) is changed to emit V3; V2 production stops going forward (new workspace clicks become V3), but **V2 consumption is kept** so any already-queued or replayed V2 events still apply.
- The consumer (`PostgresClickEventStore`) now handles `V1`, `V2`, and `V3`. All three write a `link_click_facts` row and update `link_click_hourly`; only `V3` (non-bot) also updates `link_click_dimension_daily` and `link_visitor_daily`. This satisfies "old events contribute to clicks-over-time only, with unknown referrer/device."
- `click_outbox` and `processed_click_events` get a widened `CHECK` constraint admitting `event_version = 3` under the same ownership shape as `2` (`tenant_id IS NULL AND owner_workspace_id IS NOT NULL`), plus a requirement that V3 rows carry non-null `visitor_hash`/`referrer_group`/`device_class` while V1/V2 rows keep them null. Dedup is unchanged: `processed_click_events` is still keyed on `event_id`, and the new per-event signal fields do not affect that key, so a replay of any version is still a no-op before it reaches facts/aggregates.

## Decision 2 — Visitor hash: HMAC-SHA256 with a deterministically-derived daily key

`visitorHash = truncate16(HMAC-SHA256(dayKey, ip || 0x00 || userAgent || 0x00 || mappingId))`, where `dayKey = HMAC-SHA256(baseSecret, "yyyy-MM-dd")` (UTC date). Only one secret (`VisitorHash:DailySecret`) is configured and held by the API; the day key is derived, never stored. This gives the required properties — deterministic within a UTC day, unlinkable across days — without a key-rotation job or any extra secret storage. Truncated to 16 bytes (32 hex chars). Raw IP and user agent are never stored or logged; only the resulting hash leaves the API process. The secret name and a placeholder (never a real value) are added to `docker/api.env.example` only, per the shared-environment constraint on this task.

## Decision 3 — Referrer, device, OS/browser, language classification: small tested rule set, not a new package

The handoff allows "a maintained parser package or a small tested rule set." A new NuGet dependency was rejected for Phase 1: it adds restore/build risk in a shared environment where `dotnet build`/`dotnet test` must succeed with zero warnings, it is unnecessary for the coarse buckets this page needs (Mobile/Desktop/Tablet/Bot/Unknown, OS/browser *family* only, no versions), and a small rule set is easy to unit test exhaustively. Implemented as pure static classes in `Shortener.Application`:
- `UserAgentClassifier` — bot-signature check first (library/bot/crawler/curl/wget/`python-requests`/known social-preview bots), then Tablet/Mobile/Desktop by token, OS family (Windows/macOS/iOS/Android/Linux/Unknown), browser family (Edge checked before Chrome, since Edge UA strings also contain "Chrome"; then Chrome/Firefox/Safari/Unknown).
- `ReferrerClassifier` — reduces the `Referer` host to a registrable domain by taking its last two DNS labels (a known simplification — it does not handle multi-label public suffixes like `co.uk` correctly; acceptable for Phase 1's fixed set of commercial/webmail hosts, recorded here as a limitation). A curated webmail host list is checked *before* the two-label reduction so `mail.google.com` is classified `Email`, not folded into `Google`. No path, query, or scheme is ever retained — only the reduced host.
- `AcceptLanguageParser` — takes the first comma-separated entry, strips any `;q=` weight, trims. No negotiation/ranking logic needed for a single display tag.

All four return only coarse, pre-aggregated values; none of raw IP, full user agent, or full referrer URL is ever captured in any column, log line, or test fixture (enforced by a dedicated test, see Testing below).

## Decision 4 — Click fact table + aggregates, written in one transaction

Per the amendment, `link_click_facts` is the source of truth; `link_click_hourly`, `link_click_dimension_daily`, and `link_visitor_daily` are incrementally maintained fast-path aggregates. All four writes happen in the same PostgreSQL transaction as the `processed_click_events` insert that gates deduplication, so:
- A replay is caught by the `processed_click_events` unique insert returning zero rows; nothing downstream runs.
- A crash between the four writes and the commit rolls back atomically; retry reprocesses cleanly.
- Bot events still get a `link_click_facts` row (`is_bot = true`, for transparency and because `hit_count` already counts them — that behavior is unchanged) but update none of the three aggregate tables, matching "bot events update none of them" and keeping Insights bot-free.
- `owner_workspace_id` is added to every new table (including `link_visitor_daily`, which the handoff describes only as `(mapping_id, bucket_date_utc, visitor_hash)`) because every other Phase 1 table carries it and it makes the 35-day retention sweep and any later per-workspace query a plain indexed scan instead of a join through `url_mappings`. This is additive and does not change the documented primary/unique key shape.
- `link_click_dimension_daily` has one row per `(mapping_id, bucket_date_utc, dimension, dimension_key)` with `dimension IN ('referrer','device')`; `dimension_key` is `ReferrerGroup` for the referrer dimension (not the raw host — the host lives only in facts) and `DeviceClass` for the device dimension.
- An idempotent rebuild command (`dotnet run --project src/Shortener.Analytics -- --rebuild-aggregates`) truncates and recomputes all three aggregate tables from `link_click_facts` inside one transaction. It is the documented recovery path if an aggregate table ever needs to be rebuilt (schema bug, manual correction, etc.).

## Decision 5 — Legacy (V1) and V2 events get facts/hourly rows too

`PostgresClickEventStore.ApplyAsync` resolves `owner_workspace_id` for V1 events from `url_mappings.owner_workspace_id` (already backfilled for every legacy tenant-owned managed link by the `WorkspaceManagedEvents` migration) as part of the existing ownership-verification query, so no extra round trip is needed. V1/V2 facts rows carry `is_bot = false` (unknown, not excluded — consistent with `hit_count`'s existing unconditional behavior) and null referrer/device/visitor columns.

## Decision 6 — Time zone and bucketing

All stored buckets (`bucket_start_utc` hourly, `bucket_date_utc` daily) are UTC, computed with `date_trunc('hour'|'day', occurred_at_utc)` in the same SQL statement that performs the upsert, so there is no clock skew between the application and the database. Viewer-local-time conversion for the heatmap/busiest-hour (Phase 2 UI concern) is explicitly out of scope here.

## Decision 7 — Retention

A new `VisitorRetentionWorker` hosted service in `Shortener.Analytics` runs on a fixed interval (starting immediately, then every 24h) and:
1. Deletes `link_visitor_daily` rows with `bucket_date_utc` older than 35 days.
2. Nulls `link_click_facts.visitor_hash` (row kept, hash cleared) for facts with `occurred_at_utc` older than 35 days.
Both operations log only row counts — never hashes, IPs, or user agents.

## Decision 8 — Chart library (deferred)

Blazor-ApexCharts is Phase 2's concern (the Insights page). Phase 1 ships no UI and takes no dependency on it; this ADR does not pin a package version for it.

## Privacy rules (restated, unchanged from the handoff)

- Never store, log, or publish raw IP addresses, full user-agent strings, or full referrer URLs, in any table, event, or log line.
- `visitorHash` rotates daily (Decision 2) and is truncated to 16 bytes.
- `link_visitor_daily` rows are deleted, and `link_click_facts.visitor_hash` is nulled, 35 days after capture (Decision 7).
- Guest links are never part of this pipeline (unchanged — the redirect path only computes/captures click signals for `Mode == Managed`).

## Bot policy

`UserAgentClassifier` flags bots by signature. Bot clicks still increment `hit_count` and still get a `link_click_facts` row (`is_bot = true`) for transparency, but never update `link_click_hourly`, `link_click_dimension_daily`, or `link_visitor_daily` — so every Insights metric (Phase 2) is bot-free by construction, without needing to filter at query time.

## Shared-environment constraints honored

- No Compose file, container, or private `.env` file is touched or restarted. The new `VisitorHash:DailySecret` configuration name is added only as a placeholder to `docker/api.env.example`.
- No migration is applied to `Schwab_test_local` or `Schwab_local`. The new EF Core migration is authored and exercised only inside randomly named temporary schemas created and dropped by integration tests, following the exact pattern already used by `WorkspaceLinkAccessTests`/`AccountOwnershipMigrationTests`.
- `Shortener.Web` is not touched.
