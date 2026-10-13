# Schwab URL Shortener Prototype

Updated: 2026-10-12

This repository contains a .NET 10 URL shortener prototype. Slice 1 separates anonymous seven-day guest links from Entra-authenticated, tenant-owned managed links. Storage remains in memory and resets on process restart.

## Architecture

- Domain: `src/Shortener.Domain` — `UrlMapping`, validation helpers, and business rules.
- Application: `src/Shortener.Application` — guest/managed creation, public resolution, and tenant-scoped management contracts.
- Infrastructure: `src/Shortener.Infrastructure` — in-memory storage with global short-code uniqueness.
- API: `src/Shortener.Api` — anonymous guest and redirect routes plus authenticated managed routes.
- Tests: `tests/Shortener.UnitTests` and `tests/Shortener.IntegrationTests` — focused validation around validation rules and end-to-end workflows.

## Setup

1. Restore .NET dependencies:
   `dotnet restore`
2. Configure `AzureAd__ClientId` with the API app registration's client ID. `AzureAd:TenantId` defaults to `organizations` for Entra work/school tenants. The API fails closed at startup if the client ID is missing. Register the API as multi-tenant in Entra and request an access token for its audience; do not use an ID token.
3. Run `dotnet run --project src/Shortener.Api/Shortener.Api.csproj`.
4. HTTP contract:
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

Run the solution test suite:

`dotnet test SchwabUrlShortener.slnx --nologo`

The current tests cover guest expiry, global code uniqueness, anonymous redirects, tenant isolation, and authorization. API tests use a test-only authentication handler; live Entra token validation still requires an app registration and tenant verification.

## Notes

- Persistence is in-memory for Slice 1; it is not durable or horizontally shared.
- The short codes are validated to be 4-12 characters with letters and numbers only.
- URLs are normalized to absolute `http` or `https` addresses when missing a scheme.
