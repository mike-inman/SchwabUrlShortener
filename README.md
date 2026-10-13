# Schwab URL Shortener Prototype

Updated: 2026-10-12 21:01 CDT

This repository contains a .NET 10 URL shortener prototype. Slice 1 separates anonymous seven-day guest links from Entra-authenticated, tenant-owned managed links. Slice 2 stores links and managed analytics in PostgreSQL across API restarts.

## Architecture

- Domain: `src/Shortener.Domain` — `UrlMapping`, validation helpers, and business rules.
- Application: `src/Shortener.Application` — guest/managed creation, public resolution, and tenant-scoped management contracts.
- Infrastructure: `src/Shortener.Infrastructure` — PostgreSQL repository, explicit EF Core migrations, database constraints, and atomic managed hit updates.
- API: `src/Shortener.Api` — anonymous guest and redirect routes plus authenticated managed routes.
- Tests: `tests/Shortener.UnitTests` and `tests/Shortener.IntegrationTests` — focused validation around validation rules and end-to-end workflows.

## Setup

1. Restore .NET dependencies:
   `dotnet restore`
2. Provide `ConnectionStrings__ShortenerDb` through your environment or local secret store. It must point to a PostgreSQL database; no connection string is checked in. The database role applying migrations needs permission to create the `citext` extension and schema objects.
3. Apply checked-in migrations explicitly before starting the API:
   `dotnet ef database update --project src/Shortener.Infrastructure/Shortener.Infrastructure.csproj --startup-project src/Shortener.Infrastructure/Shortener.Infrastructure.csproj --context ShortenerDbContext`
   Repeating this command applies only pending migrations. API startup never changes the schema.
4. Configure `AzureAd__ClientId` with the API app registration's client ID. `AzureAd:TenantId` defaults to `organizations` for Entra work/school tenants. The API fails closed at startup if either the client ID or PostgreSQL connection string is missing. Register the API as multi-tenant in Entra and request an access token for its audience; do not use an ID token.
5. Run `dotnet run --project src/Shortener.Api/Shortener.Api.csproj`. `GET /health/live` reports process liveness; `GET /health/ready` checks database access and pending migrations, returning 503 when unavailable or unmigrated.
6. HTTP contract:
   - `POST /api/urls/guest` with `{ "originalUrl": "https://example.com/articles/123", "shortCode": "demo1" }` creates an anonymous link with `expiresAtUtc` exactly seven days after creation. `shortCode` is optional.
   - `POST /api/urls/managed` accepts the same body and requires a valid Entra bearer access token. Tenant ownership comes from the validated token's `tid` claim.
   - `GET /r/{shortCode}` anonymously redirects active guest or managed links. Expired guest links return 404.
   - `GET /api/urls/managed/{shortCode}` and `GET /api/urls/managed/{shortCode}/analytics` require authentication and return only the caller's tenant-owned link.
   - `GET /api/urls/managed/analytics` returns only the caller's tenant-owned links. Guest links have no management or analytics surface.

Create responses contain a relative `shortUrl` such as `/r/demo1`; the serving domain is chosen by deployment. Short codes are unique across both link modes.

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

The current tests cover guest expiry, global code uniqueness and races, anonymous redirects, tenant isolation, authorization, restart persistence, concurrent managed analytics, cancellation, database constraints, and readiness failure. API tests use a test-only authentication handler; live Entra token validation still requires an app registration and tenant verification. The integration tests clear the `url_mappings` table and refuse databases whose names do not start with `schwab_slice2_test_`; use a disposable database with that prefix.

## Notes

- PostgreSQL is the only normal runtime repository. There is no in-memory fallback on connection failure.
- The short codes are validated to be 4-12 characters with letters and numbers only.
- URLs are normalized to absolute `http` or `https` addresses when missing a scheme.
