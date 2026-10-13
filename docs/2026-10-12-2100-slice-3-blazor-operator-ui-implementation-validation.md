# Slice 3 Blazor operator UI — implementation and validation

Date: 2026-10-12 21:00 CDT
Purpose: Record the Slice 3 plan, delivered UI, required configuration, local validation, and remaining live verification.

## Plan and scope

- Work only in `/Users/michaelinman/Repos/SchwabUrlShortener-slice3` on `codex/slice-3-blazor-operator-ui` from shared Slice 1 commit `805f2430189a40d4aa019368f1d80c46509fd71c`.
- Own `src/Shortener.Web/**`, isolated `tests/Shortener.WebTests/**`, `SchwabUrlShortener.slnx`, the adapted handoff, and this note. Preserve credentials, other worktrees, shared contracts, integration tests, and README.
- Replace starter pages with anonymous guest creation and a signed-in tenant dashboard. Route every link operation through the HTTP API. Keep test authentication fixtures inside the Web test project.
- Accept when the Web build, Web tests, solution build/tests, handoff check, and intended-path diff review pass. Record real Entra status separately.

## Delivered flow and decisions

- The guest page calls `POST /api/urls/guest` without a bearer token. It explains seven-day expiry and redirect-only behavior, shows the returned expiry and API-hosted public link, and offers no analytics or management controls.
- The managed dashboard requires a signed-in principal. It creates links, lists the signed-in tenant's links and redirect counts, and loads detail/analytics via `POST /api/urls/managed`, `GET /api/urls/managed/analytics`, `GET /api/urls/managed/{shortCode}`, and `GET /api/urls/managed/{shortCode}/analytics`. The UI sends no tenant selector or tenant ID; API authorization remains the boundary.
- Microsoft.Identity.Web handles Entra web sign-in and server-side delegated token acquisition. The configured API scope is requested at sign-in; tokens stay on the server and are sent only as bearer headers to the API. Sign-out is a protected form post with antiforgery protection. With Entra configuration absent, guest mode still starts and managed sign-in returns an explicit configuration error.
- The client displays status-specific, safe guidance without showing provider payloads. Server logs carry operation, failure category, and activity correlation, without tokens, cookies, or raw response bodies.
- The frozen Slice 1 API has no edit or delete route. The dashboard therefore offers creation, lookup, and analytics; editing and deleting would require a separately reviewed shared-contract decision.

## Configuration and setup

Configure these keys outside Git, with values appropriate to the real app registrations and API endpoint:

- Web: `ShortenerApi:BaseUrl`, `ShortenerApi:Scope`, `AzureAd:Instance`, `AzureAd:TenantId`, `AzureAd:ClientId`, `AzureAd:ClientSecret`.
- API: `AzureAd:ClientId`, plus the API's existing Entra authority configuration.
- Optional Web redirect configuration: `AzureAd:CallbackPath` if the app registration uses a nondefault callback path.

`ShortenerApi:BaseUrl` identifies the API origin used for API calls and public `/r/{shortCode}` links. The client accepts HTTPS, or HTTP only for a loopback address during local development. The Web app must be registered as a confidential client with a matching sign-in redirect URI, and the API's delegated scope must be exposed and consented. The server must use HTTPS for live sign-in. Store client credentials in environment-backed configuration or a secret store; do not put them in this document or tracked settings. No credentials or local passwords were changed.

## Validation and limits

- Starting state: correct Slice 3 worktree and branch, clean tracked tree, only the adapted handoff untracked, and `HEAD` equal to the shared Slice 1 baseline.
- `dotnet build src/Shortener.Web/Shortener.Web.csproj --nologo`: passed with 0 warnings and 0 errors.
- `dotnet test tests/Shortener.WebTests/Shortener.WebTests.csproj --no-restore --nologo`: passed 11 tests. bUnit tests cover guest loading/success/error and signed-in dashboard loading/error/create/detail. Isolated HTTP handlers cover anonymous guest calls, protected bearer calls and tenant-free paths, safe errors, missing API address, and rejection of nonlocal HTTP.
- `dotnet build SchwabUrlShortener.slnx --nologo`: passed with 0 warnings and 0 errors.
- `dotnet test SchwabUrlShortener.slnx --no-restore --nologo`: passed 19 unit, 3 existing integration, and 11 Web tests.
- Local Web host without Entra or API settings: anonymous `/` returned 200 and rendered the guest form; `/dashboard` redirected to sign-in; `/account/signin` returned 503 with a configuration message. No real API call was possible in that unconfigured smoke test.
- After the final sign-out change, the same unconfigured host returned 400 for a sign-out POST without an antiforgery token.
- `bash scripts/check-codex-handoff.sh docs/2026-10-12-1953-slice-3-blazor-operator-ui-handoff.md`: passed.
- Real Entra sign-in, consent, token audience/issuer validation, and protected API calls: **not verified**. No app registrations, delegated scope, tenant, or credentials were provided. Web tests use test-only principals and token providers; they do not establish live Entra behavior.
- Deployment and operational acceptance: not attempted. The shared Slice 1 baseline API still uses in-memory persistence in this worktree; Slice 2 storage changes have not been integrated here.

## Next integration action

After the independent slices are reviewed, integrate the disjoint commits, configure the real Entra applications and API origin, then verify browser sign-in, protected API access, tenant isolation, guest redirect/expiry, and managed counts against a live tenant. Review any edit/delete request as an API-contract addition before changing the UI.
