# Schwab URL Shortener

This .NET 10 solution is being developed for the AI-Proficient Software Engineer assignment. The assignment scope is a URL shortener; the broader design is production-minded and will be implemented in reviewable slices rather than treating the current starter implementation as production-ready.

## Current implementation status

- Domain and Application contain the initial mapping, validation, and create/resolve/analytics flow.
- Infrastructure and API currently use an in-memory repository. This is temporary implementation state, not the target persistence design.
- The current API exposes create, resolve-as-JSON, and analytics endpoints. Entra authorization, guest-link expiry, persistent storage, caching, and event-backed analytics are not implemented yet.
- Unit and API integration tests provide regression coverage for the current behavior; they do not establish production readiness or high-volume capacity.

## Intended architecture and product behavior

- ASP.NET Core API and Blazor Server operator UI.
- PostgreSQL as the durable source of truth; Redis as a derived redirect cache; Kafka for managed-link analytics processing.
- Entra ID protects persistent managed links and their analytics. Public redirects remain anonymous.
- Anonymous guest links are redirect-only, expire seven days after creation, and do not collect analytics or offer management.
- Persistent managed links belong to the authenticated Entra tenant and can be managed and analyzed by that tenant.
- Docker Compose will provide a local integration environment. No numeric throughput or latency target has been provided, so the project will not claim capacity based on architecture alone.

The implementation dependencies, boundaries, and Codex handoffs are documented in [the production-minded slice plan](docs/2026-10-12-1953-production-ready-slices-and-handoffs.md).

## Solution structure

- `src/Shortener.Domain` — mapping types, validation, and business rules.
- `src/Shortener.Application` — use cases and repository/service interfaces.
- `src/Shortener.Infrastructure` — persistence and external integration implementations.
- `src/Shortener.Api` — create, redirect, and analytics HTTP endpoints.
- `src/Shortener.Analytics` — future background analytics worker.
- `src/Shortener.Web` — Blazor Server UI.
- `tests/Shortener.UnitTests` and `tests/Shortener.IntegrationTests` — unit and API/infrastructure regression tests.

## Setup

1. Restore .NET dependencies:
   `dotnet restore`
2. Run the API project:
   `dotnet run --project src/Shortener.Api/Shortener.Api.csproj`
3. With the current in-memory API running, test its initial HTTP behavior:
   - `POST /api/urls` with JSON body
     `{ "originalUrl": "https://example.com/articles/123", "shortCode": "demo1" }`
   - `GET /api/urls/{shortCode}` to resolve the code as JSON
   - `GET /api/urls/{shortCode}/analytics` to review its current hit count and timestamps

The current process must remain running for the in-memory data to persist. This setup is for regression/development only; use the completed PostgreSQL and Compose slices for durable local operation.

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

The current validation covers short-code rules, URL normalization, hit counting, and a full API workflow for create/resolve/analytics.

## Notes

- Short codes are currently validated to be 4–12 alphanumeric characters.
- Scheme-less URLs are currently normalized to `https://`; only absolute HTTP and HTTPS URLs are accepted.
- Do not commit the local assignment PDF, job-description reference, environment files, or credentials.
