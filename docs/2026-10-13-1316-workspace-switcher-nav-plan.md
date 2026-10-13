# Workspace switcher navigation plan — 2026-10-13 13:16 CDT

Purpose: make the customer dashboard's workspace context compact and visible without changing workspace access or API routes.

Scope: Web dashboard/nav markup and styling, a circuit-scoped workspace navigation state, service registration, and Web component tests. Identity configuration, API client, env, Compose, and database files are outside this change.

Tasks:

1. Show a muted workspace name under `My links` when exactly one workspace is available.
2. For two or more workspaces, render an accessible compact select beside the logo in the top nav and route its change through the dashboard's existing workspace selection path.
3. Test one-workspace and multi-workspace rendering, including that switching loads the selected workspace's links.
4. Build and run relevant tests; commit only intended paths.

Acceptance: no select for one workspace; one nav select and no dashboard select for multiple workspaces; selected workspace drives the existing workspace API calls; no manual refresh is introduced.

Outcome: implemented the single-workspace caption and multi-workspace nav selector. The selector uses a circuit-scoped state callback to the dashboard's existing selection/load path; workspace API routes and identity configuration remain unchanged. Web tests passed 29/29, unit tests 25/25, and the solution build passed with 0 warnings. The Web container alone was rebuilt and `/health/ready` returned 200. A Chromium layout probe with an injected two-workspace nav measured no page overflow at 1280px or 390px; this was a CSS layout check, not an authenticated dashboard session. Live authenticated switching remains to be checked in a customer session. The full integration suite was not run because its guarded local database tests truncate link tables in the acceptance database.
