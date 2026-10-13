## Slice 3 — Blazor Server Operator UI Handoff
Status: drafted on 2026-10-12
You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Preserve all existing modified and untracked work. Do not reset, stash, discard, or stage unrelated files.
- Do not modify `.env*`, local agent configuration, credentials, or the untracked assignment PDF.
- Start only after Slice 1’s API endpoints and Entra auth contract are stable.
- This slice owns `src/Shortener.Web`; do not change API, Domain, Application, or Infrastructure contracts.

Task: Replace the generated Blazor starter screen with a guest link form and an Entra-authenticated managed-link dashboard.

Goal:
Deliver a useful server-side Blazor interface where anyone can create a seven-day redirect-only guest link, while signed-in users can create/manage persistent tenant-owned links and review their analytics.

Important context:
- `src/Shortener.Web` is currently the generated interactive-server starter app and references Application, but not Infrastructure.
- The UI must consume the API over HTTP; it must not directly construct repositories or bypass API authorization.
- Entra protects managed-link creation/management and analytics; guest creation and public redirects remain anonymous.
- Guest links expire seven days after creation and have no analytics or management features. Managed links are tenant-scoped and persistent.
- This slice is path-disjoint from the PostgreSQL Infrastructure implementation and may run in parallel with Slice 2 using a separate worktree at the shared Slice 1 commit.

In scope only:
1. Provide an anonymous guest-link form with clear seven-day expiry and no analytics/manage affordance.
2. Configure real Entra sign-in for managed-link screens using supported token acquisition for protected API calls.
3. Build a typed API client for guest creation, managed-link creation/listing/analytics, and safe error handling.
4. Replace template pages/navigation with a focused guest-link view and authenticated tenant dashboard.
5. Add UI-level tests for both modes, loading/error/success states, and authenticated API-client behavior where the existing test setup supports them.

Out of scope:
- Changes to API endpoint contracts, tenant authorization rules, or persistence.
- Direct access to PostgreSQL, Redis, or Kafka from UI components.
- Any use of in-memory application data as delivered behavior.
- Entra app registration or secret provisioning.
- QR codes, branded domains, rich device/geographic/referrer analytics, third-party integrations, and subscription-tier limits.

Required discovery before edits:
- Inspect `src/Shortener.Web/Program.cs`, project file, Razor components, styles, and appsettings.
- Read Slice 1’s API contract and authentication requirements.
- Confirm whether Slice 1 exposes the required tenant-scoped management/analytics endpoints and token audience.
- Inspect current tests and available UI/component testing patterns before adding packages.
- Review the current dirty diff for any Web files before editing them.

Implementation requirements:
- Use Blazor Server / Interactive Server and a typed `HttpClient` integration; do not reference Infrastructure from UI components.
- Keep guest creation available without sign-in, show its expiry clearly, and do not display click or management controls for it.
- Use Entra sign-in and acquire/send the API access token through the supported OIDC/Microsoft identity flow for managed links. Never place tokens in browser storage or log them.
- Hide protected management views from unauthenticated users, but treat server/API authorization—not UI visibility—as the security boundary.
- Display plain operator guidance for API failures; do not expose token, claims, stack traces, or secrets.
- Keep UI tenant context implicit in the authenticated principal; never allow selecting or submitting an arbitrary tenant ID.
- Keep public short-link navigation usable without requiring sign-in.

Exception / observability requirements:
- Log UI/API operation failures with structured, sanitized context and request correlation where available.
- Do not log access tokens, cookies, full authorization headers, or raw provider payloads.
- Show actionable messages and preserve inspectable server-side errors without returning sensitive details.

Realtime / no-polling requirements:
- Not applicable: this UI performs synchronous requests and has no asynchronous operator job status. Do not add polling or timer-based refresh.

Testing:
- Add focused tests for the typed API client and UI state transitions using isolated HTTP test handlers.
- Keep test handlers test-only; do not make them a selectable normal runtime backend.
- Build `src/Shortener.Web/Shortener.Web.csproj` and run relevant UI tests; rerun solution build/tests after integration.
- Real Entra sign-in is verified only if configured against a real tenant; otherwise identify that as an explicit blocker.

Documentation:
- Add a dated implementation/validation note with the UI flow, required configuration keys (names only), checks, and real-Entra verification status.
- Update setup documentation surgically, retaining existing assignment and architecture content.

Completion criteria:
- [ ] The starter UI is replaced with anonymous guest creation and useful signed-in management/analytics screens.
- [ ] Guest UI clearly states the seven-day expiry and exposes no analytics or edit controls.
- [ ] Protected calls use Entra-acquired API tokens and cannot choose a caller-supplied tenant.
- [ ] UI components use the API client and do not access infrastructure directly.
- [ ] Public links remain navigable without sign-in.
- [ ] Tests and documentation distinguish test-only behavior from real Entra verification.
