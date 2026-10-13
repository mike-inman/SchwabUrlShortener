# Accounts foundation decisions and threat model — 2026-10-13 06:48 CDT

## Purpose and scope

Record the ownership and security decisions required before the first personal and organization accounts schema change. This note applies only to the PostgreSQL foundation described in `2026-10-13-0636-personal-organization-accounts-foundation-handoff.md`. It does not approve a new sign-in flow, workspace authorization, an application-database migration, or deployment.

## Verified starting point

- The dedicated worktree was clean on `codex/accounts-foundation` at `3fc8fcb` when checked. `origin/main` was `624d391` and is an ancestor of that commit.
- The existing `UrlMapping` model distinguishes guest from managed links. Managed links use the validated Entra `tid` as `tenant_id`; guest links have null `tenant_id`, a seven-day expiry, and no hit count.
- Current `ShortenerDbContext` maps the mode and tenant constraint and tenant-scoped indexes. The handoff identifies Slice 5 outbox, processed-event, and dead-letter data as tenant-scoped. This note does not claim those data paths have been changed.

## Decisions for this schema stage

1. **Application identity.** Assign an immutable application user ID. Store each external identity separately with a unique pair of validated issuer and stable provider subject. Normalize and validate the issuer in the later authentication slice before lookup. Email and display name are mutable contact data, never identity keys or automatic linking evidence. No password, OAuth secret, access token, or raw invitation token belongs in these tables.
2. **Identity linking.** Multiple provider identities may reference one application user only through a later explicit, authenticated linking flow that proves control of both identities. This schema permits linking but performs none. Same-email identities remain separate until that flow succeeds.
3. **Workspace ownership.** Model personal and team workspaces separately. A personal workspace has one application-user owner. Team access requires explicit membership with constrained owner/admin/member roles; membership may span teams. A matching email domain or customer identity tenant never grants membership. Role permissions and team-creation rules require confirmation before the authorization/API slice.
4. **Invitations.** Scope each invitation to one team workspace, store a unique verifier/hash rather than a bearer token, and store expiry plus redemption state so later code can enforce one-time use. Expired or redeemed invitations cannot become active again. Token delivery, account matching, acceptance, replay handling, and administrative revoke behavior are deferred to the later API slice.
5. **Legacy Entra access.** Create one deterministic legacy team workspace per distinct existing non-null managed `tenant_id`, and backfill those mappings' nullable `owner_workspace_id`. Legacy workspaces may have no application-user memberships during this stage. Until a separately verified authorization cutover, existing validated Entra `tid` checks and tenant-scoped repository queries remain authoritative. The new column must not be interpreted as an active authorization check.
6. **Analytics compatibility.** Keep `tenant_id`, current managed writes, guest invariants, globally unique short codes, and Slice 5 `ManagedClickEventV1` and tenant-scoped outbox/aggregate behavior unchanged. Workspace-aware events and authorization require a later versioned transition.

## Threats and required controls

| Threat | Foundation control and later gate |
| --- | --- |
| Issuer confusion or subject collision | Unique validated issuer-plus-subject key; later token validation must pin issuer, audience, and delegated scope before account lookup. |
| Same-email account takeover | No automatic identity linking or membership by email/domain. Linking needs authenticated proof of both identities. |
| Cross-workspace data exposure | Explicit memberships and nullable ownership column; later API must authorize the selected workspace on every managed query and mutation. Current tenant path stays in place until that cutover. |
| Invitation leak, replay, or stale acceptance | Persist only verifier/hash, unique within the intended scheme, with expiry and single-use state; later redemption must be atomic and authorized. |
| Wrong-tenant backfill or partial migration | Deterministic tenant-to-workspace mapping in one migration transaction; test two or more tenants, count preservation, foreign keys, uniqueness, and rerun behavior against guarded `Schwab_test_local` only. Fail with sanitized diagnostics on a mismatch. |
| Lost links or analytics history | Preserve mapping IDs, destinations, hit counts, last-access timestamps, outbox, processed events, and dead letters. Compare before/after row counts and tenant associations. |
| Unsafe rollback after new account writes | Back up before production use, rehearse restore, and treat a populated new schema as a forward-repair case. Do not rely on a destructive `Down` migration. |

## Open decisions for later slices

- Confirm the customer identity provider/tenant and email registration method, including account recovery. Google sign-in remains planned, not configured.
- Confirm owner/admin/member capabilities, who may create teams, invitation eligibility, lifetime, revocation, and whether team owners may leave or transfer ownership.
- Confirm explicit identity-linking and unlinking UX and recovery policy before exposing them.
- Define and verify the legacy Entra membership cutover, including how existing tenant users are represented and when `tenant_id` can cease authorizing managed operations.
- Define the versioned analytics and cache transition to workspace ownership after API authorization exists.

## Foundation acceptance and recovery gate

Before committing schema changes, use guarded real PostgreSQL migration tests with at least two managed tenants and guests. Confirm every existing mapping and analytics row is preserved, guests remain unowned, each managed mapping points to the correct legacy workspace, database constraints reject cross-reference and invalid-state cases, and rerunning pending migrations makes no additional changes. Build and run relevant tests without touching application databases or unrelated containers. Document the exact migration and test results in a dated validation note. Any production rollout needs a separately reviewed backup, restore rehearsal, and authorization transition plan.
