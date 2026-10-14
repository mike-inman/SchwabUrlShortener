## Track D — Application Hardening Handoff
Status: drafted on 2026-10-13 19:35 CDT. **Start only after Track B has merged to `main`.**
You are working in /Users/michaelinman/Repos/SchwabUrlShortener-hardening.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Create the worktree from the main checkout after Track B merges: `git -C /Users/michaelinman/Repos/SchwabUrlShortener fetch origin && git -C /Users/michaelinman/Repos/SchwabUrlShortener worktree add /Users/michaelinman/Repos/SchwabUrlShortener-hardening -b codex/app-hardening origin/main`. Record the base commit and confirm it contains both Track A's and Track B's merges.
- Copy this handoff, `docs/2026-10-13-1720-azure-scaleout-contract.md`, and `docs/2026-10-13-1930-well-architected-review.md` from `/Users/michaelinman/Repos/SchwabUrlShortener/docs/` into the worktree's `docs/` and commit them first.
- Preserve all other worktrees. Do not modify `.env*`, credentials, local agent configuration, the assignment PDF, existing `schwab-url-shortener-*` containers and volumes, `infra/**`, or `.github/**`.
- The contract and the review are authoritative. Stop and report if either is wrong.

Task: Close the application-level Well-Architected gaps S1, S2, S3, P1, and P3: non-root containers, rate limiting, security headers, and OpenTelemetry export to Azure Monitor with end-to-end trace correlation.

Goal:
The containers run without root. Public endpoints resist abuse with per-client rate limits. Web and API send defensive security headers without breaking sign-in, the dashboard, or live updates. API, Web, and Analytics export traces, metrics, and logs to Application Insights, and one managed click can be followed from the redirect request through the outbox, Kafka, and the Analytics aggregate by a single trace ID. All of it is configuration-driven, with local defaults that keep `dotnet run`, `compose.yaml`, and `compose.existing.yaml` working.

Important context:
- `Dockerfile` has no `USER`; all runtime stages run as root. The .NET 10 `aspnet` and `runtime` images include a non-root `app` user (`APP_UID`). Track A's `Filesystem` Data Protection mode writes under the user profile, and Compose mounts key volumes at `/root/.aspnet/DataProtection-Keys`.
- No rate limiter is configured. Guest creation (`POST /api/urls/guest` and Web's guest form) is anonymous. Client IP is only trustworthy after Track A's forwarded-header trust is applied.
- Web sets only HSTS. Blazor Server needs `connect-src` for its WebSocket and must keep working with Entra sign-in redirects.
- `ClickAnalyticsMetrics` and Track B's `LiveUpdateMetrics` define meters, and the outbox stores a `trace_id`, but nothing exports telemetry. Track C sets `APPLICATIONINSIGHTS_CONNECTION_STRING` on every app. Slice 7 recorded per-event trace correlation as unverified.

In scope only:
1. Non-root containers (S1): add `USER $APP_UID` to the `web`, `api`, and `analytics` runtime stages. Ensure the Data Protection `Filesystem` path is writable by that user. Update the Data Protection volume mount paths in `compose.yaml`, `compose.scaleout.yaml`, and `compose.existing.yaml` to the `app` user's home, keeping the same named volumes so existing local keys are preserved; confirm file ownership works on a fresh and on an existing volume, and document any one-time step needed for existing local volumes.
2. Rate limiting (S2): ASP.NET Core rate limiting in API and Web, keyed on the client IP after forwarded headers. Policies, all configuration-driven under `RateLimiting__*` with local defaults in `appsettings.json`:
   - `guest-create`: fixed window, 10 requests per minute per client, on `POST /api/urls/guest` and Web's guest creation handler.
   - `redirect`: token bucket, 120 requests per minute per client with a burst of 60, on `/r/{shortCode}`.
   - Managed (authenticated) endpoints: per-user fixed window, 120 requests per minute, keyed on the validated subject claim.
   - Health endpoints exempt. Rejections return 429 with `Retry-After`, are logged once per window per client without the IP in plain text (hash it), and are counted in a metric.
   - The scale demonstration sends high load from one client IP. Provide `RateLimiting__Enabled` (default `true`) and document that Track C's scale demo either uses a dedicated, configuration-only allowance or temporarily raises the redirect limit; record the choice. Do not add an IP allow-list in code.
3. Security headers (S3):
   - Web: `Content-Security-Policy` with `default-src 'self'`, `script-src 'self'`, `style-src 'self'` (add `'unsafe-inline'` only if Blazor or Bootstrap requires it, and record why), `img-src 'self' data:`, `connect-src 'self' wss:`, `frame-ancestors 'none'`, `base-uri 'self'`, `form-action 'self'` plus the Entra authority origins required for sign-in and sign-out posts; `X-Content-Type-Options: nosniff`; `Referrer-Policy: strict-origin-when-cross-origin`; `Permissions-Policy: camera=(), microphone=(), geolocation=()`.
   - API: `X-Content-Type-Options: nosniff`, `Content-Security-Policy: frame-ancestors 'none'`, `Referrer-Policy: no-referrer`. Redirect responses keep working.
   - Remove the `Server` header in both hosts.
4. OpenTelemetry (P1, P3): add the Azure Monitor OpenTelemetry distro (`Azure.Monitor.OpenTelemetry.AspNetCore` for API and Web; the matching exporter setup for Analytics) when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, and no exporter when it is unset (local). Export ASP.NET Core, HttpClient, Npgsql, and Redis traces where instrumentation exists; the meters `Shortener.*` used by `ClickAnalyticsMetrics` and `LiveUpdateMetrics`; and logs. Set the service name per app (`shortener-api`, `shortener-web`, `shortener-analytics`). Configure sampling (`OpenTelemetry__SamplingRatio`, default `1.0` locally, `0.25` recommended for Azure) and record the reasoning against the 1 GB/day log cap.
5. Trace correlation across the pipeline: the API's redirect activity's trace context is stored with the outbox row (the existing `trace_id` field, using W3C `traceparent`), carried in the Kafka message headers by the outbox publisher, and restored as the parent of the Analytics consumer's processing activity. One click must appear as a single end-to-end trace in Application Insights.
6. Tests for each item: the container user, rate-limit rejections and exemptions, headers present on representative responses, exporter disabled without a connection string, and trace context propagated from outbox row to Kafka header to consumer activity.

Out of scope:
- Azure resources (Track C) and GitHub workflows (Track F).
- Changing authorization rules, API contracts, the schema (except as strictly needed to store `traceparent` in the existing `trace_id` column), or the Kafka event payload.
- A WAF, CAPTCHA, or IP allow-lists.

Required discovery before edits:
- Read the contract and the review.
- Inspect `Dockerfile`, all `compose*.yaml`, the three `Program.cs` files, `src/Shared/ScaleoutHostConfiguration.cs`, the outbox writer in Infrastructure, `ClickOutboxPublisher.cs`, `ClickKafkaConsumer.cs`, `ClickAnalyticsMetrics.cs`, `LiveUpdateMetrics.cs`, and the test projects.
- Check current Microsoft documentation, recording URLs, for the Azure Monitor OpenTelemetry distro on .NET 10, ASP.NET Core rate limiting with forwarded headers, and Blazor Server CSP requirements.

Implementation requirements:
- Every new behavior is configuration-driven with local defaults in `appsettings.json`; no setting is required that breaks `dotnet run` or existing Compose files.
- No secret, token, full URL with query, or raw client IP in logs or telemetry.
- Header and rate-limit middleware ordering is correct relative to forwarded headers, authentication, and static assets.

Exception / observability requirements:
- Rate-limit rejections, exporter startup state, and sampling ratio are logged once at startup or per window, structured.

Realtime / no-polling requirements:
- The CSP must allow Blazor's WebSocket. Verify the dashboard's live updates still arrive after the headers are applied.

Testing:
- Build with zero warnings; unit and Web suites; the full suite in a fresh disposable Compose project (`schwab-trackd-check`, loopback port `18094`, `deploy/network.override.example.yaml`).
- In that stack: the containers run as a non-root UID (`docker exec <container> id -u` is not 0); guest create returns 429 after the limit with `Retry-After`; headers present on Web `/`, the dashboard, and API `/r/<code>`; the Blazor live connection still works in a browser if available.
- Trace propagation: with a local OpenTelemetry collector or the console exporter in the test profile, show one trace ID spanning the redirect, the outbox publish, and the consumer processing.
- Remove the disposable project afterwards, touching nothing else.

Documentation:
- Add `docs/2026-10-13-<HHMM>-track-d-application-hardening-validation.md` with commands, results, the chosen limits and CSP, the sampling decision, and live versus test-only evidence.
- Update the review file's status column for S1, S2, S3, P1, and P3, and update README surgically.

Completion criteria:
- [ ] Containers run as non-root, with Data Protection keys preserved for existing local volumes.
- [ ] Rate limits on guest create, redirects, and managed endpoints, configuration-driven, with 429 and `Retry-After`.
- [ ] Security headers on Web and API, with sign-in, dashboard, and live updates still working.
- [ ] Telemetry exports to Application Insights when configured, with sampling, and nothing exports locally by default.
- [ ] One managed click is a single trace from redirect through the Analytics aggregate.
- [ ] Full build and test suite pass; `dotnet run` and all Compose files still start.

Report back: final branch commit, test counts, the trace evidence, and the configuration values Track C must set in Azure (`RateLimiting__*`, `OpenTelemetry__SamplingRatio`, and the scale-demo allowance). Then push and fast-forward `origin/main` when Michael approves the merge.
