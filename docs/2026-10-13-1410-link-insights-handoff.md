## Link Insights Handoff
Status: drafted on 2026-10-13 at 14:10 America/Chicago. Not implemented.
Purpose: add per-link analytics ("Ziplio Insights") with animated charts, built on the existing Kafka click pipeline and workspace authorization.

You are working in `/Users/michaelinman/Repos/SchwabUrlShortener-workspace-authorization` on `codex/workspace-authorization`.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, commit, worktree list, and `git status --short --branch` before edits. Treat changes you did not make as user-owned.
- Start only after the current fix for "Unable to acquire managed API access" (token acquisition regression after `aca3439`) is committed and verified. If Slice 7 is in progress and touches analytics, the event contract, or the dashboard, stop and report the overlap before editing.
- This handoff and its design reference were drafted in the canonical checkout. Copy `docs/2026-10-13-1410-link-insights-handoff.md` and `branding/previews/insights-preview.html` from `/Users/michaelinman/Repos/SchwabUrlShortener/` into the worktree unchanged and commit them by themselves first.
- Preserve `.env*`, private Compose env files, secrets, databases, volumes, unrelated containers, the unpublished assignment PDF, and AI-Influencer-Factory resources. Never print secrets. Migrate and test only against guarded `Schwab_test_local`; do not migrate `Schwab_local` without explicit operator approval.

## Design reference

`branding/previews/insights-preview.html` is the approved visual spec (open it in a browser). It uses ApexCharts.js 3.54.1 directly; Blazor-ApexCharts wraps the same library, so its chart options map one-to-one to `ApexChartOptions<T>`. Match the layout, chart types, colors, and copy. Its data is fictional; the real page uses API data.

## Metrics (what the page shows)

For one managed link and a selected range (`24h` hourly buckets, `7d` and `30d` daily buckets):
1. **Clicks** in range and **change versus the previous equal period** (percentage; show "New" when the previous period is zero).
2. **Unique visitors (estimated)**: distinct daily visitor hashes in range.
3. **Busiest hour**: weekday and hour with the most clicks over the last 28 days, in the viewer's local time.
4. **Last click**: time, referrer group, and device class.
5. **Clicks over time**: area chart.
6. **Top referrers**: up to 5 groups plus "Other", horizontal bars.
7. **Devices**: Mobile / Desktop / Tablet share, donut.
8. **When your audience clicks**: 7 x 24 heatmap (weekday x hour), last 28 days, viewer's local time.
9. **Links table sparkline**: 7-day daily trend per row on the dashboard.

Bot traffic is excluded from every Insights metric. Existing `hit_count` behavior stays unchanged; document that Insights counts may be lower than `hit_count` because of bot exclusion.

## Privacy rules (decide nothing looser)

- Never store, log, or publish raw IP addresses, full user-agent strings, or full referrer URLs in analytics tables or events.
- The API computes at redirect time:
  - `visitorHash` = HMAC-SHA256 over IP + user agent + link ID with a **daily rotating secret** held only by the API (configuration secret, not in the database or event store). Truncate to 16 bytes. Rotation makes hashes unlinkable across days.
  - `referrerGroup` from the `Referer` host only, normalized to a fixed set: LinkedIn (`linkedin.com`, `lnkd.in`), X (`x.com`, `t.co`, `twitter.com`), Facebook, Instagram, Email (known webmail hosts), Google, Other, Direct (no referrer).
  - `deviceClass`: Mobile, Desktop, Tablet, Bot, Unknown, from the user agent. Use a maintained parser package or a small tested rule set; record the choice in the ADR.
- Retain daily visitor-hash rows for 35 days, then delete them in a scheduled cleanup. Hourly and dimension aggregates may be kept.
- Document these rules in the ADR and validation note.

## Required discovery before edits

- Read the Slice 5 and workspace V2 event docs, `ManagedClickEventV1`/`V2` contracts, the redirect capture path, `click_outbox`, `processed_click_events`, the analytics consumer, existing migrations, workspace authorization queries, the dashboard component and its automatic-update mechanism, and Web/API test projects.
- Confirm the current event versions and that consumer deduplication by event ID covers new fields.

## In scope

1. **ADR first** (dated, in `docs/`): event versioning choice, aggregate schema, privacy rules above, bot policy, time-zone handling, chart library choice (Blazor-ApexCharts), retention.
2. **Event contract.** Add a backward-compatible versioned click event (for example `ManagedClickEventV3`) carrying `visitorHash`, `referrerGroup`, and `deviceClass` in addition to the V2 fields. Keep V1 and V2 consumption working; old events contribute to clicks-over-time only, with unknown referrer/device.
3. **Aggregates** (EF Core migration, PostgreSQL):
   - `link_click_hourly(mapping_id, owner_workspace_id, bucket_start_utc, clicks)` with a unique key on `(mapping_id, bucket_start_utc)`.
   - `link_click_dimension_daily(mapping_id, owner_workspace_id, bucket_date_utc, dimension, dimension_key, clicks)` for `referrer` and `device`.
   - `link_visitor_daily(mapping_id, bucket_date_utc, visitor_hash)` with a unique key, used for distinct counts, with 35-day retention.
   - The consumer updates these in the same transaction as its processed-event record so a replayed event never double counts. Bot events update none of them.
   - Index for range queries by `mapping_id` and time.
4. **API endpoints** (workspace-scoped, same membership predicate and non-disclosing 404 as existing workspace routes):
   - `GET /api/workspaces/{workspaceId}/urls/managed/{shortCode}/insights?range=24h|7d|30d` returns totals, previous-period total, unique visitors, the time series (UTC bucket starts), top referrers, device shares, last click, and the last 28 days of hourly UTC counts for the heatmap. Reject other `range` values with 400.
   - `GET /api/workspaces/{workspaceId}/urls/managed/sparklines?days=7` returns daily counts for every link in the workspace in one query (no per-row calls).
5. **Web.**
   - Add the `Blazor-ApexCharts` NuGet package (latest stable compatible with the solution's .NET version; record the version). It ships its JS as a static asset, so no CDN or CSP change is needed; confirm.
   - New page `/links/{shortCode}` ("Ziplio Insights"), reachable from each dashboard row's Details action. Range tabs, four stat tiles, area chart, referrer bars, device donut, heatmap, matching the design reference.
   - Chart options from the reference: transparent background, dark theme, foreColor `#9DB0CB`, dashed grid `rgba(42,63,99,.7)`, Mint `#59E6CF` series with a 0.38 to 0 gradient fill, smooth 2.6px stroke, rounded bars in mint opacity steps, donut colors Mint / `#7C93B8` / `#3A5680` with a 72% hole, heatmap ranges Quiet `#14223B`, Low `#1C4558`, Busy `#2A7F7C`, Peak Mint. Animations on (easeinout, 900 ms, dynamic 450 ms); **disabled when the viewer prefers reduced motion** (detect once via JS interop).
   - Local time: get the browser's IANA time zone once via JS interop and convert UTC buckets for the heatmap, busiest hour, and labels. Fall back to UTC and say so on the page.
   - Dashboard: add a "Last 7 days" sparkline column using a server-rendered inline SVG component (no chart library), mint when rising, `#7C93B8` when falling, with an accessible label.
   - Live updates: when the existing automatic-update path reports new clicks for the open link, refresh Insights data and call `UpdateSeriesAsync` so charts animate instead of re-rendering. No Refresh button, no new polling path.
   - Empty state for a link with no clicks: show zeroes and "No clicks yet. Share your link to start seeing activity." Charts render empty axes, not errors.
   - Amber stays reserved for the destination: the short-link dot and the last-click live dot only.
6. **Retention job** for `link_visitor_daily` (hosted service in the analytics worker), with logging of counts only.

## Out of scope

- Country or city geolocation, export, custom date ranges, per-team dashboards, Grafana or other operator observability, changes to identity, Compose topology, or deployment.

## Testing

- Unit: referrer normalization table, device classification, visitor-hash determinism within a day and difference across days, range validation, previous-period math including zero.
- Integration (guarded `Schwab_test_local`, real PostgreSQL/Kafka): a V3 event updates hourly, dimension, and visitor aggregates once; replaying the same event changes nothing; bot events are excluded; V1/V2 events still count in clicks-over-time; cross-workspace insights and sparklines requests return 404; sparklines endpoint returns all links in one call; retention removes rows older than 35 days.
- Web: Insights page renders with sample data and with no data; sparkline component emits the right trend color and label; reduced-motion flag disables animations; range tabs request the right range.
- Full build with zero warnings and full test suite with exact counts. Report skipped or blocked checks.

## Documentation and completion

- Dated ADR, dated validation note with migration evidence, privacy rules, package version, screenshots described in text (desktop 1280px and phone 390px), and remaining limits.
- Rebuild/restart only the Schwab API, Web, and Analytics containers as needed. Give the operator a browser checklist: open a link's Insights, switch ranges, click the short link from a phone and a desktop browser with a referrer, watch the page update live, confirm the sparkline column.
- Commit the copied handoff and preview first, then implementation, then the validation note. No push or deployment.

Completion criteria:
- [ ] Insights page and sparklines match the design reference and work at 1280px and 390px.
- [ ] All metrics come from aggregates updated idempotently by the consumer; bots excluded; V1/V2 compatibility kept.
- [ ] No raw IPs, full user agents, or full referrer URLs stored or logged; visitor hashes rotate daily and expire after 35 days.
- [ ] Workspace authorization enforced on both new endpoints with non-disclosing 404s.
- [ ] Reduced motion respected; live updates animate without polling.
- [ ] Tests, ADR, and validation note complete; only intended files committed.


## Amendment — 2026-10-13 14:30 CDT: collect real data first

Operator decision: real click data must start accumulating as soon as possible. This amendment overrides the ordering above and adds a click fact table.

### Phase order

- **Phase 1 — Collection (ship first, on its own):** ADR, V3 event, redirect-time capture, click fact table, hourly/dimension aggregates, consumer changes, retention job, tests, validation note, and container restart so production-shaped collection runs in the local Compose stack. No UI is required to finish Phase 1. Confirm with real redirects that rows land.
- **Phase 2 — Insights UI:** API endpoints, Blazor-ApexCharts page, sparklines, live updates. Start only after Phase 1 is committed and verified.
- **Phase 3 — Demo data (optional, last):** a script that generates realistic clicks (mixed referrers, devices, times) by calling the real redirect endpoint, so data flows through the same pipeline. It must refuse to run unless pointed at the guarded test database and loopback API. Mark generated traffic with a dedicated test referrer host so it can be identified and deleted.

### Click fact table

Add `link_click_facts`, one row per accepted non-duplicate click event, written by the consumer in the same transaction as `processed_click_events`:

| Column | Notes |
| --- | --- |
| `event_id` | Primary key; same ID as the processed event |
| `mapping_id`, `owner_workspace_id`, `short_code` | Ownership and lookup |
| `occurred_at_utc` | Click time |
| `referrer_group` | Normalized group (LinkedIn, X, Email, ...) |
| `referrer_host` | Registrable domain only (for example `linkedin.com`); never path or query |
| `device_class` | Mobile, Desktop, Tablet, Bot, Unknown |
| `os_family`, `browser_family` | Family names only (for example `iOS`, `Chrome`); no versions or full user agent |
| `language` | Primary `Accept-Language` tag (for example `en-US`) |
| `visitor_hash` | Daily-rotating HMAC as defined above; nullable for V1/V2 events |
| `is_bot` | Kept for transparency; excluded from Insights |
| `country_code` | Nullable, always null for now; reserved for a later edge/GeoIP source |
| `event_version` | 1, 2, or 3 |

Rules:
- Index on `(mapping_id, occurred_at_utc)` and `(owner_workspace_id, occurred_at_utc)`.
- Aggregates remain the fast path for the UI; the fact table is the source of truth that lets new metrics be added and recomputed later. Add a documented, idempotent rebuild command that recomputes aggregates from facts.
- V1/V2 events write facts with null dimensions so total history is preserved from Phase 1 onward.
- Retention: facts are kept (no raw IP/user agent/referrer URL is ever stored). `visitor_hash` values in facts are nulled after 35 days by the retention job, matching `link_visitor_daily`.
- Guest links stay out of analytics, unchanged.

### Added tests

- Each captured field is derived correctly from representative requests (referrer host reduction, OS/browser family, language tag, bot flag).
- A replayed event writes no second fact row and no aggregate change.
- Aggregate rebuild from facts reproduces the incrementally maintained aggregates exactly.
- No test fixture or log line contains a raw IP, full user agent, or full referrer URL.
