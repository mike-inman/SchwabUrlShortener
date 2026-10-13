# Managed dashboard live feedback plan — 2026-10-13 00:01 CDT

## Purpose

Correct misleading duplicate-link feedback, make link details unambiguous, and remove the manual Refresh control in favor of automatic updates.

## Scope

Integrated branch `codex/integrate-slices-1-3`. Update managed create API outcome, Web dashboard presentation, and live analytics delivery. Preserve local credentials and databases. Do not change deployment configuration or source worktrees.

## Tasks

1. Create a fresh random managed code on every blank-code submission, including repeated destinations. For an explicit code already associated with the same tenant and destination, return a distinct existing outcome. Preserve tenant isolation and delegated-scope authorization.
2. Show a Bootstrap-styled transient toast for created versus existing outcomes, update selected details after creation, and visually identify the selected row.
3. Deliver managed-list changes automatically to the signed-in Blazor dashboard over its existing WebSocket circuit, with a bounded server-side API check and no manual Refresh button.
4. Add focused tests for duplicate feedback and automatic update behavior, run build/tests, then verify the integrated local browser flow after restart.
5. Record implemented, tested, and remaining live-verification status here.

## Acceptance criteria

- Repeating a managed destination with no custom code creates a distinct random link. Repeating the same destination with the same explicit code says the link already exists and does not say Created.
- Creating a new destination selects its details; clicking another row clearly changes selection.
- Dashboard has no Refresh button and updates redirect count automatically while open.
- Existing Entra and tenant authorization remain in force.
- Build and relevant tests pass; live browser behavior is reported separately from code checks.

## Outcome — 2026-10-13

- Implemented on the integration branch: every blank-code managed submission creates a new random code, even for a repeated destination. Repeating the same explicit code and destination in the same tenant returns HTTP 200 and an existing-link result; a new link returns HTTP 201. Global code conflicts and delegated-scope authorization remain enforced.
- Replaced the misleading inline success banner with a Bootstrap-styled top-right toast that slides in, fades out after seven seconds, and can be dismissed. The toast distinguishes created from existing. The selected row is highlighted, and the details card follows the selected or newly created link.
- Removed the Refresh control. While the Blazor Interactive Server dashboard is open, the Web server checks the tenant-scoped API every five seconds and sends changed list and detail values to the browser through the existing Blazor live connection. This is bounded server-side sampling, not API-originated SSE. API outage is shown as a reconnecting warning. Cross-instance scaling and deployment have not been verified.
- `dotnet build SchwabUrlShortener.slnx --no-restore`: passed with zero warnings and zero errors.
- `dotnet test SchwabUrlShortener.slnx --no-restore`: 19 unit, 9 PostgreSQL/API integration, and 14 Web tests passed. The integration-test connection was assembled in process from the existing API secret store and pointed only to the guarded `Schwab_test_local` database; credentials were not displayed or changed.
- The live signed-in browser behavior of this new code remains to be checked after restarting both integrated local hosts. No push or deployment has occurred.
