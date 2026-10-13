# Schwab URL Shortener Prototype

Updated: 2026-10-13 00:44 CDT

This repository contains a .NET 10 URL shortener prototype. Anonymous guest links expire after seven days; Entra-authenticated managed links belong to the validated token's tenant. PostgreSQL stores links and managed redirect counts. Redis caches public redirect mappings. A Blazor Server app provides guest creation and a signed-in managed dashboard.

## Architecture

- Domain: `src/Shortener.Domain` — `UrlMapping`, validation helpers, and business rules.
- Application: `src/Shortener.Application` — guest/managed creation, public resolution, and tenant-scoped management contracts.
- Infrastructure: `src/Shortener.Infrastructure` — PostgreSQL repository, explicit EF Core migrations, database constraints, atomic managed hit updates, and disposable Redis redirect cache.
- API: `src/Shortener.Api` — anonymous guest and redirect routes plus authenticated managed routes.
- Web: `src/Shortener.Web` — Interactive Server UI that acquires delegated API tokens on the server and calls the API over HTTP.
- Tests: `tests/Shortener.UnitTests`, `tests/Shortener.IntegrationTests`, and `tests/Shortener.WebTests` — domain, real PostgreSQL/API, and Web regression coverage.

## Setup

1. Restore .NET dependencies:
   `dotnet restore`
2. Provide `ConnectionStrings__ShortenerDb` through your environment or local secret store. It must point to a PostgreSQL database; no connection string is checked in. The database role applying migrations needs permission to create the `citext` extension and schema objects.
3. Apply checked-in migrations explicitly before starting the API:
   `dotnet ef database update --project src/Shortener.Infrastructure/Shortener.Infrastructure.csproj --startup-project src/Shortener.Infrastructure/Shortener.Infrastructure.csproj --context ShortenerDbContext`
   Repeating this command applies only pending migrations. API startup never changes the schema.
4. Configure `AzureAd__ClientId` with the API app registration's client ID. `AzureAd:TenantId` defaults to `organizations` for Entra work/school tenants. The API fails closed at startup if either the client ID or PostgreSQL connection string is missing. Managed routes require a delegated API access token with `access_as_user`; an ID token is insufficient.
5. Provide `ConnectionStrings__Redis` through the environment or local secret store, pointing to a Redis instance accessible by the API. For a disposable loopback-only local instance, use a separate Redis container/port; do not reuse another project's Redis data. The optional `RedirectCache__Namespace` sets a safe ASCII key prefix; the default is `shortener:redirect:v1`. No Redis credential belongs in tracked settings.
6. Run `dotnet run --project src/Shortener.Api/Shortener.Api.csproj`. `GET /health/live` reports process liveness; `GET /health/ready` checks PostgreSQL access/migrations and Redis availability, returning 503 when a dependency is unavailable. Redis outage still allows redirects through PostgreSQL, while readiness reports the degraded dependency.
7. HTTP contract:
   - `POST /api/urls/guest` with `{ "originalUrl": "https://example.com/articles/123", "shortCode": "demo1" }` creates an anonymous link with `expiresAtUtc` exactly seven days after creation. `shortCode` is optional.
   - `POST /api/urls/managed` accepts the same body and requires a valid Entra bearer access token. Tenant ownership comes from the validated token's `tid` claim.
   - `GET /r/{shortCode}` anonymously redirects active guest or managed links. Expired guest links return 404.
   - `GET /api/urls/managed/{shortCode}` and `GET /api/urls/managed/{shortCode}/analytics` require authentication and return only the caller's tenant-owned link.
   - `GET /api/urls/managed/analytics` returns only the caller's tenant-owned links. Guest links have no management or analytics surface.

Create responses contain a relative `shortUrl` such as `/r/demo1`; the serving domain is chosen by deployment. Short codes are unique across both link modes.

For the Web app, configure `ShortenerApi:BaseUrl` with the API origin and `ShortenerApi:Scope` with the API's delegated scope. Configure `AzureAd:Instance`, `AzureAd:TenantId`, `AzureAd:ClientId`, and `AzureAd:ClientSecret` for the Web app registration in its separate local secret store or environment, then run `dotnet run --project src/Shortener.Web/Shortener.Web.csproj`. The Web app keeps tokens and the client secret on the server. Guest creation remains available without Web sign-in configuration. The app registration must allow the Web callback URL used locally.

## Scenarios

### Greenfield
A new repo starts empty, so we build a clean shortener from scratch with a clear layer boundary and minimal infrastructure.

### Brownfield
The solution already contains placeholder .NET scaffolding; the project is upgraded from a template to a functional prototype without large-scale refactoring.

### Ambiguous
The exact assignment details are interpreted through the governing requirement document and narrow prototypes focus on what is likely expected for evaluation: create, resolve, and analytics behaviors.

## Validation

Run the solution test suite against a real, migrated PostgreSQL test database configured via `ConnectionStrings__ShortenerDb`:

`dotnet test SchwabUrlShortener.slnx --nologo`

The current tests cover guest expiry, global code uniqueness and races, anonymous redirects, tenant isolation, authorization, restart persistence, concurrent managed analytics, cancellation, database constraints, Redis hit/miss/outage behavior, readiness failure, and Web components/API-client behavior. API tests use a test-only authentication handler. The integration tests clear the `url_mappings` table and accept only the disposable database `Schwab_test_local`. Never point them at the app database. Set `ConnectionStrings__Redis` to an isolated real Redis instance for those tests. See [the Slice 4 validation note](docs/2026-10-13-0044-slice-4-redis-implementation-validation.md) for local observations and remaining gaps.

## Notes

- PostgreSQL is the only normal runtime repository. There is no in-memory fallback on connection failure.
- Redis stores only redirect fields, with a maximum one-hour TTL. Guest TTL is shorter when required by its absolute seven-day expiry. Managed hits still update PostgreSQL on every redirect, including cache hits.
- The short codes are validated to be 4-12 characters with letters and numbers only.
- URLs are normalized to absolute `http` or `https` addresses when missing a scheme.
