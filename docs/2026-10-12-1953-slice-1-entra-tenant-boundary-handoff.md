## Slice 1 — Guest and Managed Link Security Contract Handoff
Status: drafted on 2026-10-12
You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Preserve all existing modified and untracked work. Do not reset, stash, discard, or stage unrelated files.
- Do not modify `.env*`, local agent configuration, credentials, or the untracked assignment PDF.
- Build the real Entra validation path; test-only authentication substitutes must remain isolated to tests.
- Keep this slice limited to guest/managed link behavior, tenant ownership, Entra authentication/authorization, and stable API contracts.

Task: Define and implement separate anonymous guest-link and Entra-managed link flows with safe tenant isolation.

Goal:
Make identity, guest expiry, tenant ownership, and analytics eligibility explicit in the domain/application/API boundary so storage and UI slices can build on a stable contract.

Important context:
- The current `UrlMapping` has no tenant identifier; application/repository methods are not tenant-aware.
- `src/Shortener.Api/Program.cs` currently registers in-memory storage and exposes basic create, resolve, and analytics routes.
- The API currently returns a JSON resolution response and hard-codes `https://short.ly` when composing a short URL.
- Entra is the chosen identity provider for signed-in managed links. Guest links can be created anonymously and are redirect-only for seven days.
- Managed-link creation/management and analytics require authentication; guest-link creation and public redirects do not.
- Managed links belong to exactly one validated Entra tenant. Guest links are unowned and have no analytics or management surface.
- Short codes must be globally unique across both modes for public resolution.
- Existing integration tests use an older route and payload; align them with the actual stable contract rather than preserving stale assertions.

In scope only:
1. Represent guest and managed link modes distinctly. Guest links expire exactly seven days after creation; managed links do not receive an invented expiry.
2. Add tenant ownership to managed links and make managed-link creation, management lookup, and analytics contracts tenant-scoped.
3. Add validated Entra multi-tenant authentication and tenant-context extraction for managed operations; derive tenant identity from validated claims, never request fields.
4. Define separate stable API contracts for anonymous guest creation, authenticated managed-link creation/management, public resolve/redirect, and tenant-scoped managed analytics.
5. Ensure guest links cannot be managed or queried for analytics, and expired guest links no longer redirect.
6. Add meaningful domain, application, and API authorization/expiry/isolation regression tests.

Out of scope:
- PostgreSQL implementation or schema migrations beyond defining the required domain/application fields.
- Redis, Kafka, Docker Compose, or Blazor page implementation.
- Entra app registration, credential creation, or real tenant provisioning.
- QR codes, branded domains, device/geographic/referrer analytics, third-party integrations, and subscription-tier quotas.
- Numeric performance/SLO claims or unrelated refactoring.

Required discovery before edits:
- Inspect `src/Shortener.Domain/UrlMapping.cs` and `UrlValidation.cs`.
- Inspect `src/Shortener.Application/UrlShortenerService.cs` and `src/Shortener.Application/Interfaces/`.
- Inspect `src/Shortener.Api/Program.cs`, API configuration files, project files, and both test projects.
- Inspect the assignment PDF locally for any endpoint details that affect the API contract; do not copy it into Git.
- Review the current dirty diff before editing any already-modified file. Treat its present content as baseline.

Implementation requirements:
- Use the supported ASP.NET Core/Microsoft Entra validation pattern compatible with this .NET target; validate issuer, audience, signature, and tenant claim.
- For every managed-link operation, use the tenant ID from the validated principal and pass it through the application boundary.
- Keep guest creation and public resolution anonymous. Resolve globally by short code; do not reveal tenant analytics or management data in the redirect response.
- Enforce global short-code uniqueness in the contract; tenant-scoped duplicate-original-URL lookup must not return another tenant’s mapping.
- Give guest links an absolute UTC expiry seven days after creation. Expired links must not redirect; return a safe not-found/gone response. Do not refresh expiry on a redirect or cache hit.
- Do not record click counts, last-access metadata, analytics events, or referrer/device/location data for guest links.
- Keep guest links explicitly unowned (no fabricated/default tenant). Restrict tenant ownership and analytics to managed links.
- Make missing/invalid identity fail with standard unauthorized/forbidden responses. Do not invent a default tenant, trust a query/body tenant ID, or silently fall back to in-memory/demo identity.
- Keep error responses safe and actionable; do not expose tokens, claims dumps, or credential/config values.
- Use test authentication handlers only inside regression tests, never normal runtime configuration.

Exception / observability requirements:
- Log authentication/authorization and tenant-boundary failures with structured, sanitized context; do not log tokens or raw claims.
- Include request correlation IDs where available and preserve useful exception context without leaking identity material.
- Explain missing Entra runtime configuration explicitly; never represent an unconfigured real Entra path as verified.

Realtime / no-polling requirements:
- Not applicable: this slice adds synchronous API operations and no operator-facing asynchronous progress workflow. Do not introduce polling.

Testing:
- Add focused tests proving cross-tenant management/analytics isolation, globally unique short-code behavior, and anonymous public resolution.
- Test unauthenticated protected calls and invalid tenant context.
- Run the relevant unit/integration test projects, then `dotnet build SchwabUrlShortener.slnx` and `dotnet test SchwabUrlShortener.slnx --nologo` if the targeted checks are green.
- Report separately whether tests used a test-only auth handler or a real Entra tenant; do not claim real Entra verification without credentials and a live tenant.

Documentation:
- Add a dated slice implementation/validation note with decisions, changed files, checks, and any real-Entra blocker.
- Preserve and update relevant existing documentation surgically only after reading its current contents; do not include the PDF or secrets.

Completion criteria:
- [ ] Managed-link ownership is represented and enforced by the validated Entra tenant.
- [ ] Anonymous guest creation produces redirect-only links that expire after seven days and collect no analytics.
- [ ] Managed-link creation/management and analytics are authenticated and tenant-scoped; public resolution remains anonymous.
- [ ] No caller-controlled or default tenant identity is trusted.
- [ ] Tests cover tenant isolation and authorization behavior.
- [ ] Handoff outcome distinguishes regression tests from real Entra verification.
