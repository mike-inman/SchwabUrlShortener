# Workspace authorization API/event validation — 2026-10-13 08:41 CDT

## Purpose and boundaries

Record the implementation and local evidence for the workspace authorization handoff at `664591b4b088fac9621e2a3286d85a7d3b9e7b26`. Work stayed in `/Users/michaelinman/Repos/SchwabUrlShortener-workspace-authorization` on `codex/workspace-authorization`. The branch was clean at that exact tip before edits; `c78fb77` was an ancestor. No canonical checkout, AIF resource, application database, secret store, unrelated Docker service, push, merge, or deployment was changed.

## Implemented transition

- Added `POST /api/workspaces/{workspaceId}/urls/managed` and workspace-scoped detail, per-link analytics, and analytics-list routes. The existing managed Entra tenant routes and Web client remain on their legacy path. The new API endpoints require authenticated delegated `access_as_user` and take `iss` and `sub` only from the validated principal; missing claims fail closed. The application resolves exact issuer plus subject and checks personal ownership or explicit team membership for each operation. PostgreSQL workspace reads repeat the identity and membership predicate in the query, and workspace inserts check it in the insert statement while locking the matching rows, so an earlier service check is not a reusable permission grant. Unknown workspaces and inaccessible links receive non-disclosing not-found responses. No email, domain, caller-provided user ID, or provider `tid` grants workspace access.
- Workspace managed links have a non-null `owner_workspace_id` and may have a null `tenant_id`. Legacy writes now create or reuse the deterministic legacy workspace and set `owner_workspace_id`; the new migration repairs legacy null ownership created after the foundation backfill, with count checks. Workspace reads also recognize an older null-owner row only when the selected legacy workspace has the matching tenant binding. Global short-code uniqueness and same-workspace duplicate semantics remain.
- Migration `20261013133000_WorkspaceManagedEvents` preserves link IDs, counts, history, guest rules, V1 pending outbox rows, and processed-event IDs. It adds workspace indexes and version/owner constraints to outbox and processed events. Tenant V1 records stay V1. Workspace-only redirects carry workspace ID through Redis and capture V2 events durably; publisher and Kafka consumer distinguish V1 from V2. V2 application verifies mapping ID, code, and workspace, then deduplicates by event ID. V1 verification remains tenant-scoped. Existing retry, failure, dead-letter, and correlation behavior remains in place.

## Local validation

1. `dotnet build SchwabUrlShortener.slnx --nologo`: succeeded with 0 warnings and 0 errors.
2. Focused workspace API/event integration test passed 1/1 in a random temporary schema within disposable `Schwab_test_local` on loopback port 63651; the schema was dropped after the test. It exercised two personal workspaces, two teams, owner/admin/member creates and reads, same-email identities, wrong/missing issuer or subject, missing delegated scope, cross-workspace denial, removed membership, duplicate codes, legacy null-owner repair and post-repair writes, Redis workspace metadata on port 63652, V1 pending events, V2 Kafka publishing/consumption/replay on port 63653, idempotency, and real PostgreSQL constraints. The separate resolver test also passed 1/1 in a random schema.
3. The new migration was applied only to disposable `Schwab_test_local` to run the existing full integration suite. It reported `Applying migration '20261013133000_WorkspaceManagedEvents'` and `Done.` The application database `Schwab_local` was not migrated. Full `dotnet test SchwabUrlShortener.slnx --nologo`: **60/60 passed, 0 failed, 0 skipped** (25 unit, 17 Web, 18 integration). The dedicated Schwab PostgreSQL, Redis, and Kafka container names/loopback ports were checked before testing. No unrelated container was changed.
4. Test authentication uses a `WebApplicationFactory` handler; it establishes API authorization logic only. No live Entra or Google token, external tenant, provider consent, deployment, or production behavior was verified.

## Recovery and rollout gate

The migration is forward-only because reversing it could discard workspace ownership and V2 click data. Before any application database rollout, take and verify a backup, rehearse restore, inspect tenant/workspace counts and pending outbox versions, and rehearse a count-checked forward repair on a disposable copy. A failed app rollout may roll back the application image while retaining the additive schema; database restore is a separate reviewed operation. Keep the legacy route and V1 consumer until membership migration and real-token checks pass.

## Remaining work

- Live Entra validation of issuer, audience, delegated scope, subject claim mapping, and two real identities; actual token acquisition and workspace UI remain open. The current Web client still uses legacy tenant routes and the Interactive Server dashboard keeps its existing automatic refresh behavior.
- The later customer identity phase requires a step-by-step portal walkthrough for an external tenant, app registrations, user flow, Google OAuth, and live token checks. Its separate dated plan is `docs/2026-10-13-0836-customer-identity-external-id-assessment-plan.md`; provider configuration and onboarding were not implemented here.
- Deployment, application database migration, live provider acceptance, and operational acceptance remain separate gates.
