## Track B — Dashboard Live Push Handoff
Status: drafted on 2026-10-13
You are working in /Users/michaelinman/Repos/SchwabUrlShortener-livepush.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Create the worktree first, from the main checkout: `git -C /Users/michaelinman/Repos/SchwabUrlShortener worktree add /Users/michaelinman/Repos/SchwabUrlShortener-livepush -b codex/dashboard-live-push origin/main`. Confirm `origin/main` is `66b8318a351788d6cec5ba6e3bfa3f0d0ed63ba0`; if it moved, use the new tip and record it.
- Copy this handoff and `docs/2026-10-13-1720-azure-scaleout-contract.md` from `/Users/michaelinman/Repos/SchwabUrlShortener/docs/` into the new worktree's `docs/` and commit them first as `docs: add Track B handoff and Azure scale-out contract`.
- Preserve all other worktrees. Do not edit, reset, stash, or check out anything outside this worktree.
- Do not modify `.env*`, credentials, local agent configuration, the assignment PDF, or existing `schwab-url-shortener-*` containers and volumes.
- The contract file is authoritative for every setting name, channel name, and payload field. Do not diverge; stop and report if it is wrong.

Task: Replace the dashboard's five-second server-side polling with change notifications pushed from where data changes, delivered over the existing Blazor Interactive Server connection.

Goal:
An open dashboard makes no API calls while nothing changes. When a link's analytics aggregate or a scope's link list changes, the dashboard updates within about one second plus one authorized API fetch. Authorization stays entirely in the existing API endpoints. The solution works unchanged with several Web, API, and Analytics replicas.

Important context:
- `src/Shortener.Web/Components/Pages/Dashboard.razor` runs `WatchLinksAsync` with `new PeriodicTimer(TimeSpan.FromSeconds(5))`. Each tick acquires a token and calls the managed or workspace analytics list endpoint, diffs the result, and calls `StateHasChanged()` only when it changed. `AGENTS.md` forbids manual Refresh controls; the 2026-10-13 00:01 plan recorded this timer as "bounded server-side sampling, not API-originated SSE".
- Two ownership scopes exist:
  - Workforce links: tenant scope, from the token's `tid` claim. Click events are `ManagedClickEventV1` with a tenant ID.
  - Customer links: workspace scope, from the selected workspace. Click events are `ManagedClickEventV2` with `WorkspaceId`.
- Hit counts change in the Analytics worker, not the API. `PostgresClickEventStore.ApplyAsync` commits the aggregate update and returns `true` when an event was applied, `false` when it was a duplicate. Link lists change on managed creation in the API: `POST /api/urls/managed` (tenant) and `POST /api/workspaces/{workspaceId}/urls/managed` (workspace).
- Web has no direct database access and must not get any. It reads data only through the API with the user's delegated token.
- Redis already runs in Compose and will run in Azure as Azure Managed Redis, which is clustered. Classic `PUBLISH`/`SUBSCRIBE` on one channel works across a cluster; pattern subscriptions are avoided for that reason.
- Track A runs in parallel and will add Redis to Web for the token cache. Register `IConnectionMultiplexer` with `TryAddSingleton` so both registrations coexist. Track A merges first; you rebase on it.

In scope only:
1. Contract: `ILinkChangePublisher` in `src/Shortener.Application` with `PublishAsync(LinkChange change, CancellationToken)`, where `LinkChange` is `(LinkChangeScope Scope, Guid ScopeId, string ShortCode)` and `LinkChangeScope` is `Tenant` or `Workspace`. Serialize exactly the contract payload: `{ "v": 1, "scope": "tenant"|"workspace", "scopeId": "<guid>", "shortCode": "<code>" }`.
2. Publisher: `RedisLinkChangePublisher` in `src/Shortener.Infrastructure`, publishing to `LiveUpdates__Channel`. Best-effort: on failure, log and count, never throw to the caller.
3. Publish points:
   - Analytics: after `ApplyAsync` returns `true` (applied, committed, not a duplicate). V1 → tenant scope; V2 → workspace scope. Publishing never blocks or fails event processing or offset commits.
   - API: after a managed create returns Created (not AlreadyExists), for both the tenant and workspace endpoints.
4. Web subscriber: `src/Shortener.Web/Services/LiveUpdates/`:
   - A singleton hosted service holding one Redis subscription per Web instance.
   - `Subscribe(LinkChangeScope scope, Guid scopeId, Func<Task> onChange)` returning `IDisposable`; in-process fan-out to matching subscribers only.
   - On Redis connection restored, invoke every subscriber once to resynchronize. Expose connected or disconnected state so the dashboard can show its existing warning.
   - Registration through a single `AddLiveUpdates(IConfiguration)` extension, so each host's `Program.cs` change is one line.
5. Dashboard:
   - Remove `WatchLinksAsync` and the `PeriodicTimer`.
   - Subscribe for the current scope after first render: tenant from the user's `tid` claim for workforce identity; the selected workspace ID for customer identity. Only subscribe to scopes taken from the signed-in user's own claims or the workspace list the API returned for them, never from the URL or query.
   - Resubscribe when the selected workspace changes; unsubscribe in `DisposeAsync`.
   - Coalesce signals to at most one fetch per `LiveUpdates__DebounceMilliseconds` per circuit, then reuse the existing refresh path (fetch through the API, diff, `StateHasChanged()` only on change).
   - Show the existing "Live updates are temporarily unavailable. Reconnecting automatically." banner while the subscription is disconnected. Do not fall back to polling.
   - Keep the "Updates automatically" label, reauthentication handling, toasts, and selection behavior unchanged.
6. Compose: only the hunks the contract assigns to Track B (`LiveUpdates__*` on `web`, `api`, `analytics`, and `ConnectionStrings__Redis` on `analytics`). If you need Web connected to Redis before Track A merges, add exactly the contract's Track A lines for `web`, so the merge is identical text.

Out of scope:
- Data Protection, token cache, forwarded headers, Kafka security, Analytics health endpoints (Track A).
- Azure resources (Track C).
- Changes to API authorization, tenant or workspace rules, analytics endpoints, schema, migrations, outbox, or Kafka contracts.
- SSE endpoints, an API-hosted SignalR hub, or any browser-side connection other than the existing Blazor circuit.
- Guest links. They have no dashboard and produce no notifications.

Required discovery before edits:
- Read the contract in full.
- Inspect `Dashboard.razor` (timer, scope selection, workspace switching, reauth, disposal), `WorkspaceNavigationState.cs`, `ShortenerApiClient.cs`, `ManagedApiTokenProvider.cs`, `src/Shortener.Api/Program.cs` create endpoints, `src/Shortener.Analytics/ClickKafkaConsumer.cs`, `Worker.cs`, `PostgresClickEventStore.cs`, `RedisRedirectCache.cs`, and the Web, unit, and integration test projects.
- Confirm how the dashboard determines workforce versus customer identity and the current workspace ID.

Implementation requirements:
- Signals carry no destination URLs, counts, user IDs, or tokens. A forged or misrouted signal can at worst cause one extra authorized fetch.
- One Redis subscription per Web instance, not per circuit.
- Subscriber callbacks run through the component's `InvokeAsync`; exceptions in one subscriber never affect others or the subscription.
- Idle cost is zero API calls and zero token acquisitions.
- No new timer-based refetching anywhere. A debounce delay that runs only after a signal is not polling.

Exception / observability requirements:
- Metrics: `shortener.live.published`, `shortener.live.publish_failed`, `shortener.live.received`, `shortener.live.dispatched`, and a connection-state gauge.
- Logs carry scope kind and correlation or trace ID where available; never scope IDs alongside user identifiers, and never payloads beyond the contract fields.
- Publish failures in Analytics or API log once per failure with exception type, without retry loops.

Realtime / no-polling requirements:
- This track removes the only operator-facing polling loop. After it, `rg "PeriodicTimer" src/Shortener.Web` returns nothing.
- Delivery is push over Redis to the Web server, then over the existing Blazor Interactive Server WebSocket to the browser.

Testing:
- Unit tests: payload serialization matches the contract exactly; fan-out delivers only to matching scope and ID; one failing subscriber does not stop others; debounce coalesces a burst of 50 signals into one fetch; reconnect triggers exactly one resync per subscriber; disposal unsubscribes.
- Integration tests against real Redis from the Compose test profile: publish from one `RedisLinkChangePublisher`, receive in a separately constructed subscriber; tenant and workspace isolation.
- Analytics test: applied event publishes once; duplicate event publishes nothing; publisher failure does not prevent the offset commit.
- Web component test: the dashboard makes no API calls without signals over a simulated interval, and exactly one fetch after a burst of signals.
- Build: `MSBUILDDISABLENODEREUSE=1 dotnet build SchwabUrlShortener.slnx --nologo -m:1 /nodeReuse:false` with zero warnings.
- Full suite in a fresh disposable Compose project with the test profile. Use project name `schwab-trackb-check`, loopback port `18093`, and `deploy/network.override.example.yaml`.
- End-to-end in that stack: insert a test-only managed row as in the Slice 6 record, redirect it, and observe `shortener.live.received` increment in Web with exactly one dashboard-path fetch. If live Entra sign-in is unavailable, state that the signed-in browser view was not exercised.
- Remove the disposable project with `down --volumes --remove-orphans` afterwards, touching nothing else.

Documentation:
- Add `docs/2026-10-13-<HHMM>-track-b-dashboard-live-push-validation.md` with the design, payload, failure behavior, commands, results, and live versus test-only evidence.
- Update the 2026-10-13 00:01 live-feedback plan's outcome section surgically to say polling was replaced, linking the new note. Update README surgically for `LiveUpdates__*`.

Completion criteria:
- [ ] The dashboard has no timer and makes no API calls while idle.
- [ ] Analytics publishes after applied aggregate commits; API publishes after managed creates; both scopes covered.
- [ ] Web holds one Redis subscription per instance and fans out by scope.
- [ ] Bursts coalesce to one fetch; reconnect resynchronizes once; disconnection shows the existing warning without polling.
- [ ] Tenant and workspace isolation proven against real Redis.
- [ ] Full build and test suite pass.
- [ ] Only contract-assigned files and hunks changed; after rebasing on Track A, each host's `Program.cs` gains only the `AddLiveUpdates(...)` line.

Report back: final branch commit, test counts, end-to-end evidence, and whether the rebase onto Track A was clean.
