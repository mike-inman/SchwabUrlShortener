## Track A — Scale-out Readiness Handoff
Status: drafted on 2026-10-13
You are working in /Users/michaelinman/Repos/SchwabUrlShortener-scaleout.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Create the worktree first, from the main checkout: `git -C /Users/michaelinman/Repos/SchwabUrlShortener worktree add /Users/michaelinman/Repos/SchwabUrlShortener-scaleout -b codex/azure-scaleout-readiness origin/main`. Confirm `origin/main` is `66b8318a351788d6cec5ba6e3bfa3f0d0ed63ba0`; if it moved, use the new tip and record it.
- Copy this handoff and `docs/2026-10-13-1720-azure-scaleout-contract.md` from `/Users/michaelinman/Repos/SchwabUrlShortener/docs/` into the new worktree's `docs/` and commit them first as `docs: add Track A handoff and Azure scale-out contract`.
- Preserve all other worktrees. Do not edit, reset, stash, or check out anything outside this worktree.
- Do not modify `.env*`, credentials, local agent configuration, the assignment PDF, or existing `schwab-url-shortener-*` containers and volumes.
- The contract file is authoritative for every setting name and value. Do not rename settings. If the contract is wrong, stop and report rather than diverge.

Task: Make API, Web, and Analytics safe to run as multiple replicas by moving per-container state to shared stores, making proxy trust network-based, adding Event Hubs-compatible Kafka settings, and giving Analytics HTTP health endpoints.

Goal:
Every application container is replaceable. A user signed in to Web stays signed in, with API tokens intact, when the replica serving them is killed. Multiple API and Analytics replicas run without conflict. All Azure-specific behavior is chosen by configuration, and local Compose keeps working with local values.

Important context:
- Data Protection keys are stored per container on the `api_keys` and `web_keys` volumes. With several replicas, each replica has its own keys, so cookies issued by one are rejected by another.
- Web uses `.AddInMemoryTokenCaches()` in `src/Shortener.Web/Program.cs`. Tokens are lost when a replica restarts or scales in, forcing `/account/renew`.
- `ConfigureTrustedProxy` in both `src/Shortener.Web/Program.cs` and `src/Shortener.Api/Program.cs` resolves `ForwardedHeaders:KnownProxyHost` by DNS once at startup with `ForwardLimit = 1`. In Azure there is no `proxy` host; traffic arrives through Front Door and then Container Apps ingress from inside `10.60.0.0/23`.
- `AnalyticsOptions` builds Kafka producer, consumer, and admin configs with only `BootstrapServers`. The Analytics `--healthcheck` path builds an `AdminClient` the same way. Event Hubs' Kafka endpoint requires SASL_SSL.
- The Analytics image uses `mcr.microsoft.com/dotnet/runtime:10.0` and has no HTTP server. Container Apps health probes are HTTP or TCP only.
- The outbox publisher already uses `FOR UPDATE SKIP LOCKED`, so multiple Analytics replicas are safe for publishing. Consumer parallelism is bounded by partition count; that is Track C's concern.
- Track B (dashboard live push) runs in parallel and will add one `AddLiveUpdates(...)` line to the Web and API hosts after you merge. Keep the startup code tidy so that line rebases cleanly.

In scope only:
1. Data Protection, API and Web: implement `DataProtection__Mode` = `Filesystem` | `AzureBlob` per the contract. `Filesystem` keeps today's path. `AzureBlob` uses `PersistKeysToAzureBlobStorage(BlobUri, DefaultAzureCredential)` and `ProtectKeysWithAzureKeyVault(KeyVaultKeyId, DefaultAzureCredential)`. Always call `SetApplicationName(DataProtection__ApplicationName)`. Missing or partial settings fail at startup naming the setting.
2. Token cache, Web: implement `TokenCache__Mode` = `InMemory` | `Distributed`. `Distributed` uses `.AddDistributedTokenCaches()` with `AddStackExchangeRedisCache` (`Configuration` from `ConnectionStrings:Redis`, `InstanceName` from `TokenCache__InstanceName`) and sets `MsalDistributedTokenCacheAdapterOptions.Encrypt = true`, which uses the shared Data Protection keys from item 1. Register any `IConnectionMultiplexer` with `TryAddSingleton` so Track B's registration does not conflict.
3. Forwarded headers, API and Web: support `ForwardedHeaders__KnownNetworks` (comma-separated CIDRs) as an alternative to `KnownProxyHost`, and `ForwardedHeaders__ForwardLimit` (default `1`). At most one of the two trust settings may be set. Extract the shared logic into one helper per host or a small shared file; do not change behavior when only `KnownProxyHost` is set.
4. Kafka, Analytics: add `Kafka__SecurityProtocol`, `Kafka__SaslMechanism`, `Kafka__SaslUsername`, `Kafka__SaslPassword`, and `Kafka__EnableIdempotence` per the contract. Apply them to producer, consumer, and admin clients, including the `--healthcheck` path. `SaslSsl` without username or password fails at startup. Never log the password.
5. Event Hubs idempotence: check Microsoft's current documentation for Event Hubs Kafka support of idempotent producers. Record the finding, with the URL, in the validation note. Keep `EnableIdempotence` default `true` for local Kafka; record the value Track C should set for Event Hubs.
6. Analytics health: serve `GET /health/live` and `GET /health/ready` on port `8080`, with readiness performing the same PostgreSQL and topic-metadata checks as `--healthcheck`, bounded to the same timeouts. Use the smallest change that works; switching the Analytics Dockerfile stage to `mcr.microsoft.com/dotnet/aspnet:10.0` and hosting a minimal Kestrel endpoint is acceptable. Keep `--healthcheck` working for Compose.
7. Compose: edit only the hunks the contract assigns to Track A. Add `compose.scaleout.yaml` and `deploy/Caddyfile.scaleout` that run 2 API and 2 Web replicas behind Caddy. API upstreams use round-robin. Web upstreams use cookie-based sticky routing (`lb_policy cookie`) with dynamic DNS upstreams so both replicas are discovered. Keep the Caddyfile routing identical to `deploy/Caddyfile.local` otherwise.
8. Tests for each mode switch, the forwarded-headers combinations, and Kafka config construction.

Out of scope:
- Any Azure resource, Bicep, or `azd` file (Track C).
- Dashboard polling or live updates (Track B). Do not touch `Dashboard.razor`.
- Changes to authorization, tenant or workspace rules, schema, migrations, redirect cache, outbox, or consumer logic.
- PostgreSQL Entra authentication, Event Hubs OAuth, Azure SignalR Service.
- Removing or renaming any existing setting or Compose service.

Required discovery before edits:
- Read the contract in full.
- Inspect `src/Shortener.Web/Program.cs`, `src/Shortener.Api/Program.cs`, `src/Shortener.Analytics/Program.cs`, `AnalyticsOptions.cs`, `AnalyticsDependencyMonitor.cs`, `Dockerfile`, `compose.yaml`, `compose.local.yaml`, `compose.test.yaml`, `deploy/Caddyfile.local`, and the three test projects.
- Check current package versions and choose compatible versions of `Azure.Extensions.AspNetCore.DataProtection.Blobs`, `Azure.Extensions.AspNetCore.DataProtection.Keys`, `Azure.Identity`, and `Microsoft.Extensions.Caching.StackExchangeRedis`, aligned with the existing `StackExchange.Redis 2.13.17` and .NET 10.
- Confirm Microsoft.Identity.Web's distributed cache API for the version in use.

Implementation requirements:
- No silent fallback between modes. Misconfiguration fails at startup with the setting name and no secret values.
- Local Compose defaults (`Filesystem`, `Distributed` on local Redis, `KnownProxyHost=proxy`, `Plaintext`) must keep the existing stack working with no `.env` changes.
- Token cache entries are encrypted at rest in Redis.
- Do not weaken host filtering. `AllowedHosts` behavior stays as today; Azure values come from configuration.

Exception / observability requirements:
- Startup logs one structured line per host naming the selected Data Protection mode, token cache mode, and forwarded-header trust mode, without URIs that contain secrets.
- Redis or Blob/Key Vault failures during startup surface as startup failures, not degraded success.
- Readiness failures in Analytics log dependency name and exception type only.

Realtime / no-polling requirements:
- Not applicable. Do not add polling. Health endpoints are bounded probes.

Testing:
- Unit tests: each mode's valid and invalid configuration; forwarded-headers rules (proxy host only, networks only, both rejected, neither); Kafka configs carry SASL settings into producer, consumer, and admin configs; secrets absent from exception messages.
- Build: `MSBUILDDISABLENODEREUSE=1 dotnet build SchwabUrlShortener.slnx --nologo -m:1 /nodeReuse:false` with zero warnings.
- Full suite in a fresh disposable Compose project with the test profile, as in the Slice 6 and Slice 7 records. Use project name `schwab-tracka-check`, loopback port `18091`, and `deploy/network.override.example.yaml`.
- Scale-out proof in a fresh disposable project `schwab-tracka-scale` on loopback port `18092` with `compose.scaleout.yaml`:
  - Confirm 2 API and 2 Web replicas healthy.
  - Guest create and redirect succeed repeatedly across API replicas.
  - Data Protection: a cookie or antiforgery token issued through one Web replica validates on the other. Force this by stopping one Web replica mid-session and confirming the browser session continues on the other.
  - Token cache: if live Entra sign-in is available locally, sign in, stop the serving Web replica, and confirm the dashboard loads without `/account/renew`. If live sign-in is not available, prove it with an integration test against real Redis that a token written by one process instance is read by another, and state plainly that live sign-in was not exercised.
  - Analytics: `curl http://<analytics>:8080/health/ready` returns 200 with dependencies up, and non-200 with Kafka stopped.
- Remove both disposable projects with `down --volumes --remove-orphans` afterwards, touching nothing else.

Documentation:
- Add `docs/2026-10-13-<HHMM>-track-a-scaleout-readiness-validation.md` with commands, results, the Event Hubs idempotence finding and URL, and which checks were live versus test-only.
- Update README surgically: the new settings, `compose.scaleout.yaml`, and the Analytics health endpoints.

Completion criteria:
- [ ] Data Protection keys are shared through configuration, with `Filesystem` local and `AzureBlob` for Azure.
- [ ] Web's token cache is distributed and encrypted, and survives loss of a Web replica.
- [ ] Forwarded-header trust works by network range with a configurable forward limit; existing proxy-host behavior is unchanged.
- [ ] Analytics Kafka clients, including the health check, support SASL_SSL; idempotence for Event Hubs is documented with a source.
- [ ] Analytics serves HTTP health endpoints on 8080.
- [ ] The 2 API + 2 Web local scale-out stack passes the checks above, with live versus test-only evidence stated.
- [ ] Full build and test suite pass; local Compose works unchanged with existing env files.
- [ ] Only contract-assigned files and hunks changed.

Report back: final branch commit, test counts, scale-out results, and the recommended `Kafka__EnableIdempotence` value for Event Hubs.
