# Slice 1 guest and managed link security — implementation and validation

Date: 2026-10-12 20:39 CDT
Purpose: Record the plan, API/security decisions, local checks, and remaining verification for the Guest and Managed Link Security Contract handoff.

## Plan and scope

- Work only in `/Users/michaelinman/Repos/SchwabUrlShortener-slice1` on `codex/slice-1-guest-managed-security`.
- Copy only the 14 user-authorized baseline files from the canonical checkout after checking destination ownership; leave the canonical checkout, credentials, local configuration, and assignment PDF untouched.
- Implement guest expiry and tenant-owned managed links through Domain, Application, in-memory Infrastructure, and API.
- Add Entra bearer validation and test-only authentication for API regression tests.
- Accept when guest links redirect anonymously for seven days without analytics, managed operations are tenant-scoped, globally unique codes are enforced, and targeted and solution checks pass.

## Decisions and implementation

- Guest links have `TenantId = null`, an absolute UTC `ExpiresAtUtc = CreatedAtUtc + 7 days`, and no hit tracking. At the expiry instant, public resolution returns 404. Managed links have a nonempty tenant GUID, no invented expiry, and hit counts on public redirects.
- The repository's atomic `TryAddAsync` enforces case-insensitive global short-code uniqueness in the in-memory implementation. Managed original-URL reuse is scoped by tenant. The future durable repository must retain these contracts and enforce uniqueness in storage.
- `Microsoft.Identity.Web` 4.16.0 configures Entra JWT bearer validation using the `organizations` tenant authority and the configured API client ID. Managed endpoints require authorization; each operation extracts a nonempty `tid` GUID from the validated principal. A missing or malformed tenant produces 403. No tenant is accepted from a request body or query. The API refuses to start without `AzureAd:ClientId` and never installs a runtime test identity.
- `POST /api/urls/guest` and `GET /r/{shortCode}` are anonymous. `POST /api/urls/managed`, `GET /api/urls/managed/{shortCode}`, `GET /api/urls/managed/{shortCode}/analytics`, and `GET /api/urls/managed/analytics` require authentication. The public response is an HTTP redirect without tenant or analytics fields. Create responses use relative paths to avoid hard-coded or untrusted hostnames.
- Authentication and tenant-boundary failures log a sanitized reason and correlation ID. Unhandled failures produce a generic problem response and a server-side exception record. Invalid URL and duplicate-code errors produce safe 400/409 responses.
- The assignment PDF was read locally and contains no prescribed endpoint paths. It was not copied. The copied `README.md` was updated for this contract; no other pre-existing project document was changed.

## Files changed

- Copied baseline: the 14 exact files authorized by the user, including the Slice 1 handoff, architecture documents, README, source, project, and existing test files. The copied infrastructure project file retains its baseline Npgsql reference; no PostgreSQL work was started.
- Slice 1 implementation: `src/Shortener.Domain/UrlMapping.cs`, `UrlValidation.cs`; `src/Shortener.Application/Interfaces/IUrlShortenerRepository.cs`, `IUrlShortenerService.cs`, `UrlShortenerService.cs`; `src/Shortener.Infrastructure/InMemoryUrlShortenerRepository.cs`; `src/Shortener.Api/Program.cs`, `Shortener.Api.csproj`, `appsettings.json`.
- Validation and setup: `tests/Shortener.UnitTests/UnitTest1.cs`, `tests/Shortener.IntegrationTests/UnitTest1.cs`, `Shortener.IntegrationTests.csproj`, `README.md`, and this note.

## Checks and outcome

- `dotnet build src/Shortener.Api/Shortener.Api.csproj --no-restore --nologo`: passed (0 warnings, 0 errors).
- `dotnet test tests/Shortener.UnitTests/Shortener.UnitTests.csproj --nologo`: passed, 19 tests.
- `dotnet test tests/Shortener.IntegrationTests/Shortener.IntegrationTests.csproj --no-restore --nologo`: passed, 3 tests after adding its missing test-host dependency and arranging test configuration before the startup guard.
- `dotnet build SchwabUrlShortener.slnx --nologo`: passed (0 warnings, 0 errors).
- `dotnet test SchwabUrlShortener.slnx --nologo`: passed (19 unit tests and 3 integration tests).
- Real Entra: not verified. No live app registration client ID, real tenant access token, or tenant provisioning was provided. The API's normal path is configured to use Entra; integration tests substitute a test-only authentication handler.
- Deployment and operational acceptance: not attempted. In-memory storage loses links on restart.

No commit or merge was made.
