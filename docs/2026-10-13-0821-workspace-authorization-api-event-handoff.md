## Workspace Authorization API and Event Transition Handoff
Status: drafted on 2026-10-13
Drafted at 08:21 America/Chicago; not yet dispatched.
Purpose: implement the next personal and organization account stage after the completed ownership-schema foundation.
You are working in /Users/michaelinman/Repos/SchwabUrlShortener-workspace-authorization.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm physical path, branch, commit, `origin/main`, worktree list, and `git status --short --branch` before edits. The last implementation commit before this handoff is `c78fb77c1ec50303c53a848ecfe61190412b9017`. Start from the committed handoff tip and verify that commit is its ancestor. Stop and reconcile if the branch or worktree has changed; never reset or overwrite local work.
- Preserve `.env` files, API keys, passwords, local user-secrets, agent configuration, databases, all unrelated Docker services, the canonical checkout's untracked files, and the unpublished assignment PDF. Never print or commit secret values. Use only the dedicated Schwab test services and guarded `Schwab_test_local` database for write tests.
- Keep the assignment within the scope below. Use narrow validation after the first substantive edit. Review staged paths and diff before committing only intended work.

Task: Connect validated individual identities and explicit workspace membership to managed-link API operations, while introducing workspace-owned persistence and versioned click analytics without breaking the existing Entra tenant path.

Goal:
A personal workspace owner and every team owner/admin/member can create and read managed links and analytics only in their selected workspace. A different user, a same-email identity, a missing membership, or a wrong issuer/subject cannot gain access. Existing validated Entra tenant routes and V1 analytics continue to work until a separately verified cutover.

Important context:
- The 06:36 accounts foundation handoff was completed on `codex/accounts-foundation` and validated locally. Its schema and migration are ancestors of this branch at `772f903`; do not dispatch or repeat that old handoff. See `docs/2026-10-13-0756-accounts-foundation-migration-validation.md`.
- `docs/2026-10-13-0805-workspace-authorization-plan.md` records the product rule: an application user is an individual; an organization account is a team workspace containing users. Team owner, admin, and member may all create/read links and analytics. The first exact issuer-plus-subject membership resolver and guarded real-PostgreSQL test were committed at `c78fb77` and full local tests passed 58/58. The resolver is not connected to managed API routes yet.
- Current `/api/urls/managed` routes authorize from validated Entra `tid` and delegated `access_as_user`; `UrlMapping`, PostgreSQL repository queries, Redis redirect targets, `ManagedClickEventV1`, outbox, and consumer still assume tenant-owned managed links. Legacy mapped workspaces may have no individual memberships. Do not mistake the schema or the resolver test for live authorization.
- The current canonical checkout and draft PR #1 are separate. The accounts and workspace branches have not been pushed or merged. Do not modify the canonical checkout, accounts branch, or AI-Influencer-Factory resources.

In scope only:
1. Add explicit workspace selection to new managed API operations. Extract issuer and stable subject only from a successfully validated access token, enforce the configured issuer/audience/delegated scope, resolve an application user by exact issuer plus subject, and authorize personal ownership or explicit team membership server-side for each create, detail, and analytics request. Fail closed with safe responses. Do not provision/link users from email, domain, or customer `tid`.
2. Add workspace-scoped application and repository operations for managed links. Preserve global short-code uniqueness, duplicate semantics within the selected workspace, guests, public redirects, and existing link IDs. Keep the current Entra tenant routes functional during transition. Close the gap where legacy API writes can leave `owner_workspace_id` null after the one-time foundation backfill; verify old links remain visible to the intended workspace without cross-tenant access.
3. Stage the PostgreSQL constraints/indexes needed for personal and nonlegacy team links whose ownership is a workspace rather than an Entra tenant. Maintain a forward-repair/backup recovery policy; never clear an application database or weaken guest invariants.
4. Introduce a versioned workspace-aware managed-click event and compatible outbox/publisher/consumer/processed-event transition. Continue consuming and deduplicating V1 tenant events. Update redirect-cache metadata so a workspace-owned managed redirect never loses attribution or silently skips durable click capture. Preserve existing counts, history, idempotency, and failure/dead-letter behavior.
5. Add focused tests and dated validation evidence, then run solution build and tests. Commit only this slice's intended files on `codex/workspace-authorization`.

Out of scope:
- No public email registration, Google federation, new identity-provider configuration, personal/team onboarding UI, invitation acceptance or emails, account linking/unlinking, membership administration, workspace switcher, live Entra cutover, production/app database migration, deployment, branch push/merge, Docker reset, or Kubernetes provisioning.
- Do not remove the current legacy Entra API path until membership migration and live-provider checks are independently verified. Do not reinterpret a shared identity-provider tenant as an organization membership.
- Do not alter unrelated Docker services or AI-Influencer-Factory files, secrets, ports, containers, or volumes.

Required discovery before edits:
- Inspect this handoff, `AGENTS.md`, `docs/2026-10-13-0805-workspace-authorization-plan.md`, the account foundation decision/threat model and validation note, `src/Shortener.Api/Program.cs`, `src/Shortener.Domain/UrlMapping.cs`, application service/repository interfaces, PostgreSQL repository/context/migrations, `IRedirectCache` and Redis cache, `ManagedClickEventV1`, outbox publisher, Kafka consumer/event store, current Web API client, and guarded integration-test setup.
- Identify exact old and new write paths, all managed read routes, every cached redirect and click capture path, and current legacy rows with nullable workspace ownership. Check target files for local changes before editing.

Implementation requirements:
- Treat token validation as a prerequisite to identity lookup. Do not accept caller-provided issuer, subject, user ID, role, or tenant ID as authority. Use exact verified issuer and stable provider subject; reject ambiguity, missing claims, unknown identity, and missing membership. Keep authorization in the server-side operation path so a prior access check cannot become a stale permission grant.
- Apply workspace constraints to every managed query and write, including duplicate-code lookup and analytics. A team member may create/read links; owner/admin-only management actions are outside this slice. Return non-disclosing denial or not-found responses for inaccessible links.
- Keep legacy tenant compatibility explicit and testable. New workspace ownership must not make V1 events, old cached targets, or pending outbox rows invalid. Version new events; never silently reinterpret V1 tenant IDs as workspace IDs. Any data migration must be idempotent, count-checked, and tested in a disposable schema before app-database consideration.
- Do not weaken authentication, database constraints, cache validation, analytics durability, or retry behavior to satisfy a test.

Exception / observability requirements:
- Keep sanitized error responses and structured logs with request/click correlation IDs where available. Log operation and safe failure category, never access tokens, connection strings, invitation values, personal profile data, or raw credentials. Database, Redis, and Kafka failures must remain inspectable without falsely reporting successful analytics.

Realtime / no-polling requirements:
- Preserve the Blazor Interactive Server dashboard's automatic updates. This slice does not add an operator UI or a Refresh button. If an existing dashboard path changes, its current live update behavior and workspace boundary must remain intact; document any UI work deferred to the later sign-up/UI slice.

Testing:
- Start with focused unit/API and guarded real-PostgreSQL tests. Include two users with the same contact email but different identities, two personal workspaces, two teams, all three team roles, removed membership, wrong/missing issuer or subject, cross-workspace reads and writes, old links with null workspace ownership, guests, duplicate short codes, V1 pending events, V2 workspace events, Kafka replay/idempotency, and Redis cached redirects. Test real database constraints and migration rerun in random temporary schemas inside only `Schwab_test_local` on loopback port 63651.
- Existing full-suite integration tests use dedicated Schwab Redis and Kafka on loopback ports 63652 and 63653. Verify exact targets before tests that write; do not truncate `Schwab_local` or any application database. Run focused checks, `dotnet build SchwabUrlShortener.slnx`, and `dotnet test SchwabUrlShortener.slnx`. Distinguish test authentication handlers from live Entra/Google validation, and report counts, warnings, skipped tests, and remaining cutover gates.

Documentation:
- Update the dated plan and add a dated validation note under `docs/` with purpose, schema/event transition, recovery/rollback approach, exact tests and environments, and unfinished identity/UI/live-provider/deployment work. Keep the historical foundation handoff and its validation intact. Check the staged diff for secrets or unrelated files before committing.

Completion criteria:
- [ ] Every new workspace-managed API operation authorizes the validated individual against the selected personal or team workspace at the operation boundary; no access is inferred from email, domain, `tid`, or client-supplied user ID.
- [ ] Owner, admin, and member can create/read in their team, a personal owner can create/read only personally, and cross-workspace/same-email/removed-member cases are denied without data disclosure.
- [ ] Legacy Entra routes, existing links, V1 click events, pending outbox, guest behavior, and public redirects remain compatible during the staged transition; new workspace-owned clicks use a versioned durable event path and preserve idempotent analytics.
- [ ] Guarded real-service tests, full build/test gates, dated validation, recovery notes, and an intended-file commit are complete. No application database, secret, unrelated service, canonical checkout, push, merge, or deployment was changed.
