# Workspace authorization plan — 2026-10-13 08:05 CDT

## Purpose

Move managed-link authorization from provider tenant ownership toward explicit application-user and workspace membership, building on account foundation commit `772f903`. Preserve the existing validated Entra path until a verified legacy-membership cutover. This is a SchwabUrlShortener plan only; AI-Influencer-Factory resources are unrelated.

## Starting state and ownership

- Worktree: `/Users/michaelinman/Repos/SchwabUrlShortener-workspace-authorization`, branch `codex/workspace-authorization`, starting at `772f903b6234f3b2f2fca6686251bb83dfc22948`. It was clean when checked on 2026-10-13.
- Account foundation defines application users, external identities, personal/team workspaces, team memberships, invitations, and nullable `url_mappings.owner_workspace_id`. Legacy managed mappings were backfilled by Entra tenant. These tables do not yet authorize API calls.
- Current managed API, application service, PostgreSQL repository, redirect cache, and `ManagedClickEventV1` use `tenant_id`. The existing Entra API policy requires a validated delegated `access_as_user` token.
- Preserve the accounts worktree, canonical checkout, untracked user files, secrets, all databases, and unrelated Docker services. Do not deploy, push, or alter live Entra configuration in this slice.

## Product rule confirmed 2026-10-13

An application user identifies one person. A personal account has one user's personal workspace; an organization account is a team workspace containing individual users. Team owner, admin, and member roles may all create managed links, view links, and view analytics within that team. Personal workspace access belongs to its owner. Team membership management, invitations, ownership transfer, and team creation remain later policy/API work; no access is inferred from email, email domain, or a shared identity-provider tenant.

## Implementation sequence

1. Define a server-side workspace access context and policy. Resolve a validated token by configured issuer plus stable subject to an application user. Accept an explicitly selected workspace ID; authorize personal ownership or explicit active team membership on every workspace-managed operation. Missing identity, missing selection, and missing membership fail closed. Never auto-link accounts by email.
2. Introduce workspace-scoped managed service and PostgreSQL queries for create, detail, and analytics. Retain global short-code uniqueness and guest behavior. Keep the legacy tenant-scoped API on its existing verified path during transition; do not silently treat legacy backfill as membership.
3. Design and implement the schema and event transition needed for personal and nonlegacy teams, where provider tenant ID cannot represent ownership. Version analytics events and cache metadata compatibly; verify existing V1 outbox and consumer behavior before cutover. Preserve mapping IDs and analytics history.
4. Add meaningful API, service, and real PostgreSQL tests for two users, personal/team isolation, all three team roles, absent/revoked membership, same-email identities, wrong issuer/subject, legacy Entra behavior, guests, and event compatibility. Use only guarded disposable `Schwab_test_local` resources; never clear application databases.
5. Run focused tests, then the solution build and test gates. Review staged paths and diff, document exact evidence and remaining live identity/cutover checks, and commit only intended files on this branch.

## Acceptance gates

- No managed operation grants workspace access solely from `tid`, email, domain, or client-supplied user ID.
- Authorized team members and personal owners can create/read only in their selected workspace; unrelated users receive a safe denial with no link details.
- Existing Entra tenant behavior and V1 analytics remain available until an explicit, tested cutover; guest links and public redirects retain their behavior.
- No secrets, database contents, unrelated containers, canonical checkout files, or accounts foundation changes are lost.

## Current status

The first authorization increment adds `IWorkspaceLinkAccess` and a PostgreSQL implementation that resolves an exact issuer-and-subject identity and checks personal ownership or explicit team membership. It is not connected to managed API endpoints yet, and the legacy Entra route is unchanged. No account is automatically created or linked.

The focused integration test used a fresh temporary schema in the guarded `Schwab_test_local` database on loopback port 63651, then dropped that schema. It passed 1/1 and covered personal ownership, owner/admin/member team access, same-email separation, issuer separation, missing identities, and removed membership. The complete solution built with 0 warnings and 0 errors after restoring its declared packages. The full local suite passed 58/58: 24 unit, 17 Web, and 17 integration tests, using only dedicated Schwab PostgreSQL, Redis, and Kafka services on loopback ports 63651, 63652, and 63653. API-path authorization checks remain pending.

Next: connect validated access-token identity and selected workspace to managed API operations, preserving legacy behavior, then implement workspace-scoped persistence and versioned analytics before any cutover.

Live Entra, Google, deployment, production migration, and operational acceptance are not claimed.
