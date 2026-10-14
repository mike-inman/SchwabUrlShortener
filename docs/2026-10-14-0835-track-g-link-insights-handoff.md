## Track G — Ziplio Insights (charts and metrics) Handoff
Status: drafted 2026-10-14 08:35 CDT. **Start only after Track D has merged to `main`.**
You are working in /Users/michaelinman/Repos/SchwabUrlShortener-insights.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, `git worktree list`, and `git status --short --branch` before edits.
- Copy this handoff, `docs/2026-10-13-1720-azure-scaleout-contract.md`, and `docs/2026-10-13-1930-well-architected-review.md` from `/Users/michaelinman/Repos/SchwabUrlShortener/docs/` into the worktree's `docs/` and commit them first.
- Preserve all other worktrees, `.env*`, private Compose env files, secrets, databases, volumes, unrelated containers, the assignment PDF, and AI-Influencer-Factory resources. Never print secrets. Migrate and test only against disposable or guarded test databases; never migrate an existing local application database without explicit operator approval.
- Do not edit `infra/**` or `.github/**`. Report what Track C or Track F must change.
- Every new setting needs a local default in `appsettings.json` (or `appsettings.Development.json` where the value must never reach production), and `dotnet run` plus every Compose file must still start. Two regressions came from required settings with no default.

Task: Finish and ship "Ziplio Insights": privacy-first per-link click analytics with animated charts, built on the existing Kafka click pipeline and workspace authorization.

Goal:
A signed-in user opens **Insights** for any managed link and sees the approved design: clicks over time, change versus the previous period, estimated unique visitors, busiest hour, last click, top referrers, device share, and a weekday-by-hour heatmap, all updating live as clicks arrive. The links table shows a 7-day sparkline per row. No raw IP, full user agent, or full referrer URL is ever stored, logged, or published.

Important context:
- **Design spec:** `branding/previews/insights-preview.html` (ApexCharts 3.54.1). Match its layout, chart types, colors, and copy; its data is fictional.
- **Original handoff and ADR (authoritative for metrics and privacy):** `docs/2026-10-13-1410-link-insights-handoff.md` (including its 14:30 amendment) and `docs/2026-10-13-1500-link-insights-collection-adr.md`.
- **Phase 1 code exists but is unverified.** Commit `7542692` on `claude/link-insights-collection` preserved 23 previously uncommitted files as-is ("wip"): `ManagedClickEventV3`, `ReferrerClassifier`, `UserAgentClassifier`, `AcceptLanguageParser`, `VisitorHashService`, `ManagedClickSignal`, `AggregateRebuildService`, `VisitorRetentionWorker`, migration `20261013150000_LinkInsightsCollection`, and edits to the redirect capture path, outbox, consumer, event store, and tests. It was written on base `e7e2456`, before Tracks A, B, and D. It has never been built or tested on current `main`.
- **What changed on `main` since then:** Track A (scale-out configuration in all three hosts), Track B (live updates: `ILinkChangePublisher`, Redis publisher, the consumer publishes after applied aggregate commits, the dashboard subscribes and throttles), Track B's startup-default fix, Track D (vulnerable package pins, non-root containers, rate limiting, security headers including CSP, OpenTelemetry, `www` redirect, favicon). Expect conflicts in `Shortener.Analytics/Program.cs`, `ClickKafkaConsumer.cs`, `PostgresClickEventStore.cs`, `Shortener.Api/Program.cs`, and the Web dashboard.
- The Azure environment is live at `https://ziplio.io` in customer identity mode (External ID). Migrations run in Azure through the `caj-shortener-migrate` job. Secrets live in Key Vault; Track C owns all Azure changes.

In scope only:

Part 1 — Bring Phase 1 onto current `main`:
1. Rebase `claude/link-insights-collection` onto `origin/main` (or recreate it from `origin/main` and port the Phase 1 changes, if a rebase is unworkable). Keep the original authorship of Phase 1 changes. Resolve conflicts preserving every behavior from Tracks A, B, and D, especially: the live-update publish after an applied aggregate commit must still happen exactly once per applied event, now including V3 events; duplicate and bot events publish nothing.
2. Re-check the migration against the current model snapshot and the migrations added since `e7e2456`; regenerate its designer or snapshot if needed. Confirm it applies cleanly to a fresh database and to a database already at current `main`'s latest migration.
3. `VisitorHash:DailySecret` (base secret, at least 32 random bytes): local default only in `appsettings.Development.json` with an obviously non-production value; the API refuses to start in a non-Development environment if the value is missing, shorter than 32 bytes, or equal to that development value. Never log it. Report to Track C: Key Vault secret `visitor-hash-secret`, readable only by `id-shortener-api`, mapped to `VisitorHash__DailySecret`.
4. Complete and pass the Phase 1 tests in the ADR, including the privacy test that scans persisted rows, events, and captured logs for raw IP, user agent, and referrer URL values.

Part 2 — Insights API (workspace-scoped, same membership predicate and non-disclosing 404 as existing workspace routes):
5. `GET /api/workspaces/{workspaceId}/urls/managed/{shortCode}/insights?range=24h|7d|30d` returns: total clicks, previous-period total, unique visitors (estimated), time series (UTC bucket starts; hourly for `24h`, daily otherwise, zero-filled), top 5 referrer groups plus Other, device shares, last click (time, referrer group, device class), and the last 28 days of hourly UTC counts for the heatmap. Other `range` values return 400. Bots are excluded everywhere.
6. `GET /api/workspaces/{workspaceId}/urls/managed/sparklines?days=7` returns zero-filled daily counts for every link in the workspace in one query. `days` is limited to 1–30.
7. Both endpoints are covered by Track D's per-user rate limit, use indexed range queries, and return within a reasonable bound on a workspace with 1,000 links and 100,000 clicks (seed this in an integration test and record the timing; no numeric SLO claim).
8. Workforce (tenant-scoped) links are out of scope for Insights; the Web app runs customer mode in Azure. Return 404 for legacy routes rather than inventing tenant analytics.

Part 3 — Insights page:
9. Add the chart library through a pinned, maintained NuGet package (`Blazor-ApexCharts`, version recorded in the ADR) with its JavaScript served from the app's own static assets, never a CDN, so Track D's CSP (`script-src 'self'`) holds. If the library needs any CSP relaxation, stop and report the exact directive instead of loosening the policy.
10. Route `/dashboard/links/{shortCode}/insights` (authorized, workspace from the existing selected-workspace state; never from the URL alone). Match the design spec: range selector (24h, 7d, 30d), stat tiles (clicks with change, unique visitors, busiest hour, last click), area chart, referrer bars, device donut, heatmap. Busiest hour and heatmap are shown in the viewer's local time zone, converted in the browser or from a time-zone offset the browser supplies; the server stores and returns UTC only.
11. Dashboard links table: an **Insights** action per row, and a 7-day sparkline per row from the sparklines endpoint (one request per table render, not per row).
12. Live updates: when Track B's `LinkChange` signal arrives for the open link's scope, refresh the Insights data through the existing throttle path; no new timer or polling.
13. Accessibility: each chart has a text alternative (a visually hidden summary or data table), tiles use real headings, color is not the only carrier of meaning, and the page works at 320 px wide.
14. Empty and error states: a link with no clicks shows a friendly empty state, not empty axes; API failure shows the existing alert pattern.

Part 4 — Delivery:
15. Tests: unit (classifiers, hashing, bucketing, previous-period math, zero-filling, local-time conversion), integration (endpoint authorization including cross-workspace denial, range validation, bot exclusion, replay without double counting, V1/V2/V3 mixing), and Web component tests (page renders tiles and charts from a stubbed API, empty state, live-update refresh, sparkline batching).
16. Build with zero warnings; full suite in a fresh disposable Compose project (`schwab-trackg-check`, loopback `18095`, `deploy/network.override.example.yaml`); confirm `dotnet run` of Web and API and every Compose file start.
17. End-to-end in that stack: create a managed link through the API test path, send redirects with a mix of referrers and user agents including one bot, and confirm the Insights endpoint counts them correctly and excludes the bot.
18. Report to Track C, for the Azure rollout: the Key Vault secret, the API app setting, that the migration job must run before the new images serve traffic, and any new metric or log that should feed the workbook. Report to Track F anything CI needs (for example a longer integration timeout).

Out of scope:
- Raw IP, full user agent, or full referrer storage, geolocation, per-visitor tracking, cookies or client-side tracking scripts.
- Changing `hit_count` semantics (Insights may be lower because bots are excluded; document this on the page).
- Infrastructure (`infra/**`) and CI (`.github/**`) edits.

Documentation:
- Update the ADR with Phase 2 decisions (chart library and version, time-zone handling, endpoint shapes, CSP outcome).
- Add `docs/2026-10-14-<HHMM>-track-g-link-insights-validation.md` with commands, test counts, timings, screenshots of the page if a browser is available, and live-versus-test-only evidence.
- Update README surgically: what Insights shows and its privacy rules.
- Update the Well-Architected review if anything in it changes.

Completion criteria:
- [ ] Phase 1 rebased onto current `main`, conflicts resolved without losing Tracks A, B, or D behavior; migration verified on fresh and current databases.
- [ ] Insights and sparkline endpoints workspace-scoped, rate limited, tested, bot-free.
- [ ] Insights page matches the design spec, updates live, is accessible, and passes the CSP unchanged.
- [ ] Privacy test proves no raw IP, user agent, or referrer URL is persisted, emitted, or logged.
- [ ] Full build and test suite pass; `dotnet run` and all Compose files start.
- [ ] Azure rollout steps handed to Track C; CI needs handed to Track F.

Report back: final branch commit, test counts, validation note path, and the exact Track C and Track F follow-ups. Ask Michael before pushing to or fast-forwarding `main`.
