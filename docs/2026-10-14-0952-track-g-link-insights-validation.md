# Track G link Insights validation

Date: 2026-10-14 09:52 CDT

Worktree: `/Users/michaelinman/Repos/SchwabUrlShortener-insights`

Branch: `claude/link-insights-collection`

## Implemented

Phase 1 V3 click collection was rebased onto Track D's `origin/main` while retaining V1/V2 consumption, applied-only live signals, bot exclusion, rate limits, headers, and scale-out configuration. The API validates a 32-byte visitor secret at runtime, with a Development-only placeholder. Migration-only startup does not require the runtime secret. UTC bucketing is explicit in writes and rebuilds.

The customer-workspace Insights and batched sparkline endpoints use membership checks and the existing managed rate policy. The Web page has range selection, tiles, local-time heatmap, four chart types, text alternatives, empty/error states, reduced-motion support, and workspace-scoped live refresh. The dashboard makes one sparkline request for the selected workspace per list load or live refresh. The chart package is pinned and served from the Web origin; the CSP was not changed.

## Local evidence

- `dotnet restore SchwabUrlShortener.slnx --nologo`, then `dotnet build SchwabUrlShortener.slnx --no-restore -v:q` on the final source: 0 warnings, 0 errors.
- Focused Insights integration in isolated Compose: 7/7 passed. It covered a fresh schema, upgrade from current main's migration, V3/replay/bot/V2, aggregate rebuild, 35-day hash retention, workspace authorization, invalid ranges, and the indexed scale read.
- API-level integration created a workspace link through the authenticated route, sent two human redirects with distinct user agents/referrers and one bot redirect, applied the captured V3 outbox events, then read two non-bot clicks and two estimated visitors from the Insights endpoint. The test checked outbox, fact/event data, and captured API log messages for raw input leakage.
- Scale test seeded **1,000 workspace links and 100,000 click facts** in a temporary schema, rebuilt aggregates, then measured a 7-day per-link read at **80 ms** and one batched sparkline read at **232 ms** in the disposable Docker test container. These are observed timings, not a service-level objective. The full seed/rebuild/test took 36 seconds.
- `dotnet run` API and Web started in Development with explicit disposable connection settings; `/health/live` on API and `/` on Web each returned 200. The existing Redis connection setting is required for plain host startup. No application database was migrated by these host starts.
- `docker compose -p schwab-trackg-check` with the temporary override on `10.251.48.0/24` started db, Redis, Kafka, migration, API, Web, Analytics, and proxy. All long-running services reached healthy state. Loopback `http://localhost:18095/` and the locally served ApexCharts module returned 200. The Web CSP retained `script-src 'self'` with its nonce. No other Compose project was modified.
- Base + local + test, base + cloud, and base + scaleout Compose combinations passed `config --quiet` with the temporary override. Cloud and scaleout were syntax checked only. The existing and HTTPS variants require private environment or certificate files and were not started or altered.
- No authenticated browser session was available in the disposable stack, so a screenshot and real-browser chart/CSP acceptance remain open. Component tests render the chart page and alternatives with a stubbed API; this is not browser acceptance.

## Final checks

- `dotnet test SchwabUrlShortener.slnx --nologo -m:1 /nodeReuse:false -v:q` in the isolated Compose test container on the final source: **39/39 integration, 43/43 unit, 69/69 Web** tests passed (151 total).
- Final `docker compose -p schwab-trackg-check ... up -d --build api web analytics` rebuilt and started only the Track G project. API, Web, Analytics, PostgreSQL, Redis, Kafka, and proxy all reported healthy. Final `http://localhost:18095/` and the ApexCharts module returned 200; `script-src 'self'` and the nonce remained in the CSP header. The temporary network's inspected subnet was `10.251.48.0/24`.
- `git diff --check`: passed. Staged path/diff and final commit/status are checked separately before handoff.

## Rollout and ownership

**Track C:** Create Key Vault secret `visitor-hash-secret` with at least 32 random bytes; grant only `id-shortener-api` read access; map it to API setting `VisitorHash__DailySecret`. Confirm both the secret and API reference are deployed before requesting any push or fast-forward to `main`. Run the `caj-shortener-migrate` job before new API images serve traffic. Check existing click-consumed, deduplicated, outbox-age, and consumer-lag signals in the workbook; verify the 35-day visitor-hash retention worker and signed-in Insights page in Azure. Local tests do not prove Azure deployment or live ingestion.

**Track F:** Keep the full disposable Compose integration suite in CI, with a unique project name and isolated subnet. Allow for the 100,000-click seed/rebuild (about 36 seconds in this run), plus existing Kafka failure-path tests and image build time. No `.github/**` change was made here.

**Release gate:** Michael's confirmation of the Track C Key Vault secret and API reference deployment is required before asking to push or fast-forward `main`. The branch remains local until that gate and review.
