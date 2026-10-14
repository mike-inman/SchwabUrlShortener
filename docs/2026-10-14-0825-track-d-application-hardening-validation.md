# Track D application hardening validation — 2026-10-14 08:25 CDT

## Scope and commits

Worktree: `/Users/michaelinman/Repos/SchwabUrlShortener-hardening`, branch `codex/app-hardening`. Item 0 is the separate package remediation commit `514bf64`, already fast-forwarded to `origin/main` with Michael's approval. The unmerged favicon fix `c9ce88a` was cherry-picked separately as `8ebdb18` before the application hardening commit. Items 1–7 are local work pending final validation and a separate approval before another push to main.

## Implementation

- API, Web, and Analytics runtime images run as `APP_UID` (1654 on the tested .NET image). API and Web create a writable `/home/app/.aspnet/DataProtection-Keys`; Compose keeps the named key volumes at that path. The existing-volume transition retains the key files and requires a backed-up, one-time `chown` if they are root-owned.
- API `guest-create` is 10 requests in 60 seconds per forwarded client IP; redirect uses a 60-token burst and 60 tokens per 30 seconds; managed endpoints use 120 requests in 60 seconds per validated issuer and subject. Web's guest form now uses static server rendering, so normal submission is an HTTP POST covered by the same per-client fixed window. Health routes have no policy. `RateLimiting__Enabled=true` by default. Rejections include 429, `Retry-After`, a counter, and a hashed client-key log entry once per minute. For Track C's single-client scale demo, raise the redirect token limit and refill rate only in that disposable environment, then restore defaults. No code allow-list was added.
- Web sends nonce-based script CSP with `default-src 'self'`, `img-src 'self' data:`, `connect-src 'self' wss:`, no framing, and configured Entra form origins. `style-src 'unsafe-inline'` remains for Blazor reconnect UI styling; Google Fonts CSS and font origins are included because `App.razor` loads them. API sends no-framing CSP; both hosts send `nosniff` and Referrer-Policy, and disable Kestrel's Server header. The favicon files are same-origin and covered by `img-src 'self'`.
- `APPLICATIONINSIGHTS_CONNECTION_STRING` selects Azure Monitor OpenTelemetry for all three hosts. Empty is the local default and selects no exporter. ASP.NET Core, HttpClient, Npgsql, custom Redis spans, logs, and `Shortener.*` meters are configured as applicable. Service names are `shortener-api`, `shortener-web`, and `shortener-analytics`. `OpenTelemetry__SamplingRatio` defaults to `1.0`; Track C should begin Azure at `0.25` and review ingestion against the 1 GB/day cap. `OpenTelemetry__ConsoleExporter=false` is a local diagnostic switch, not for Azure.
- The API persists W3C `traceparent` in the existing outbox `trace_id`; the outbox publisher starts a producer span and adds a Kafka `traceparent` header; the consumer restores that parent and starts a consumer span over aggregate application. The event payload and database schema are unchanged.
- Both API and Web redirect `www.` plus the configured `PublicLinks:BaseUrl` host to the HTTPS apex with path and query preserved. Health is exempt. Local HTTP loopback public origins remain valid for existing development setups.

### Configuration for Track C

Every new option has an `appsettings.json` default in its host. Keep `RateLimiting__Enabled=true`. The API and Web defaults are `RateLimiting__GuestCreate__PermitLimit=10`, `RateLimiting__GuestCreate__WindowSeconds=60`, `RateLimiting__Redirect__TokenLimit=60`, `RateLimiting__Redirect__TokensPerPeriod=60`, `RateLimiting__Redirect__ReplenishmentSeconds=30`, `RateLimiting__Managed__PermitLimit=120`, and `RateLimiting__Managed__WindowSeconds=60`. Set `PublicLinks__BaseUrl=https://ziplio.io/` on both hosts in Azure so `www.ziplio.io` redirects to the apex. Set `APPLICATIONINSIGHTS_CONNECTION_STRING` on all three hosts and start with `OpenTelemetry__SamplingRatio=0.25`; compare daily ingestion with the 1 GB/day cap before adjusting. Keep `OpenTelemetry__ConsoleExporter=false` in Azure. For the scale demo, raise the redirect token limit and refill rate in the demo configuration and restore the normal values afterward.

## Local evidence

- `dotnet build SchwabUrlShortener.slnx -warnaserror --no-restore`: succeeded with 0 warnings and 0 errors on the final application source. `dotnet list SchwabUrlShortener.slnx package --vulnerable --include-transitive`: no vulnerable packages in any of the nine projects.
- Focused Web hardening tests: 5 passed, including the four favicon routes and CSP. Focused API hardening tests: 4 passed. Unit tests: 30 passed. Full Web tests after the guest form changed: 64 passed with a disposable Redis.
- `dotnet run --no-build --no-launch-profile`: API, Web, and Analytics each returned 200 on `/health/live` with a disposable Redis and dummy database/Kafka configuration. After the favicon cherry-pick, a running Web app served `/favicon.ico`, `/favicon.png`, `/favicon.svg`, and `/apple-touch-icon.png` with HTTP 200, and its asset CSP contained `img-src 'self' data:`.
- A real HTTP POST to the server-rendered guest form returned 200, preserved the submitted URL in the returned form, and displayed an actionable API-configuration error in the intentionally unconfigured local Web host. The focused test sends the antiforgery token and form handler and observes 429 on its second submission.
- Disposable portable Compose previously started all seven services healthy; API, Web, and Analytics returned UID 1654. A fresh named key volume was owned by 1654; a disposable previously root-owned volume retained its sentinel and became writable after a one-time ownership change. An HTTP guest-create probe returned ten 201 responses followed by 429 and `Retry-After`.
- A disposable Caddy proxy served an apex Web page, redirected `www.localhost` paths and queries to HTTPS apex, and completed the BlazorPack WebSocket handshake. An interactive signed-in dashboard and live Entra authentication were not available for browser verification. This is a transport check, not an end-to-end live update assertion.
- A real Kafka integration test persisted the incoming W3C trace ID in `click_outbox.trace_id`, asserted a producer span's trace ID matched it, asserted the consumer span's parent was that producer span, and verified the aggregate hit count changed from 0 to 1. This is local trace continuity, not observed Application Insights ingestion.
- No `schwab-url-shortener-*` project container, existing volume, `.env` file, credential, infrastructure file, or workflow was changed.

### Compose startup matrix

| Inputs in disposable projects | Result |
| --- | --- |
| `compose.yaml` + `compose.local.yaml` + `compose.test.yaml` + `deploy/network.override.example.yaml` | Full test service exited 0 on the final source: integration 32/32, unit 30/30, Web 64/64. API, Web, and Analytics started as UID 1654 in a separate runtime pass; guest API gave ten 201 then 429. |
| `compose.yaml` + `compose.local.yaml` + `compose.scaleout.yaml` + network override | API/API2, Web/Web2, Analytics, PostgreSQL, Redis, Kafka, and Caddy started. Five app containers were UID 1654; `/` and `/health/live` through Caddy returned 200. Replica images used the base app image tags from the same build. |
| `compose.yaml` + `compose.cloud.yaml` + temporary loopback port/network override | Full stack started, with HTTPS Web `/` and API `/health/live` both 200 through Caddy after readiness. API, Web, Analytics were UID 1654. The port override avoided binding host 80/443. |
| `compose.existing.yaml` + temporary isolated names, volumes, and network override | PostgreSQL, Redis, Kafka, API, Web, Analytics started. API readiness and Web HTTP liveness returned 200; all three app containers were UID 1654. The public origin was local HTTP loopback. |
| `compose.existing.yaml` + `compose.https.yaml` + same isolated override | Web was recreated with an ephemeral local PFX and served `/health/live` over internal HTTPS port 8443 with 200. The certificate and disposable resources were removed after the check. |

The Compose commands used a generated disposable PostgreSQL password and dummy Entra client ID. Only the disposable `Schwab_test_local` database was migrated. The existing-bundle clone used separate external volumes named `schwab-trackd-existing-*`; neither the user's fixed containers nor their volumes were targeted. Compose configuration also passed `docker compose config --quiet` for the base, local, scaleout, cloud, test, existing, and existing+HTTPS combinations.

The repeatable check sequence was `dotnet build SchwabUrlShortener.slnx -warnaserror --no-restore`, `dotnet list SchwabUrlShortener.slnx package --vulnerable --include-transitive`, then `docker compose -p schwab-trackd-check -f compose.yaml -f compose.local.yaml -f compose.test.yaml -f deploy/network.override.example.yaml --profile test build tests`, `up -d --no-build tests`, `docker wait schwab-trackd-check-tests-1`, and `down --volumes --remove-orphans`. The Compose commands require disposable `POSTGRES_PASSWORD`, `API_CLIENT_ID`, `PUBLIC_BASE_URL`, `PUBLIC_DOMAIN`, and `LOCAL_PORT` environment values. The local run used port 18094 and a generated password, without writing a private environment file.

## Final checks

The final fresh Compose test service exited 0: 32 integration, 30 unit, and 64 Web tests passed. The final source built with zero warnings and no vulnerable NuGet packages in the nine projects. The staged paths and diff check included only Track D source, tests, Docker/Compose, README, and dated docs; no `.env`, credential, infrastructure, or workflow path was staged. Disposable projects and volumes were removed. Actual Azure export, provider sign-in, signed-in dashboard rendering, and live update delivery in a browser remain for deployment acceptance; the browser automation surface was unavailable locally.

## Documentation used

- ASP.NET Core rate limiting and middleware order: https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0 and https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0
- Forwarded headers: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0
- Blazor CSP: https://learn.microsoft.com/en-us/aspnet/core/blazor/security/content-security-policy?view=aspnetcore-10.0
- Blazor static SSR form binding: https://learn.microsoft.com/en-us/aspnet/core/blazor/forms/binding?view=aspnetcore-10.0
- Azure Monitor OpenTelemetry distro and sampling: https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.AspNetCore/README.md and https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-configuration
- Npgsql instrumentation: https://www.nuget.org/packages/Npgsql.OpenTelemetry
