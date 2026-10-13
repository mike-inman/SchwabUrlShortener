# Personal and organization accounts plan — 2026-10-13 05:41 CDT

## Purpose and status

Plan self-service personal accounts and organization teams for SchwabUrlShortener, including email registration and Google sign-in, without changing the current Slice 5 Kafka work. This is an architecture plan, not an implemented or deployed authentication feature.

## Current boundary

- The Blazor Web app signs in with Microsoft Entra through `Microsoft.Identity.Web`; the API validates delegated `access_as_user` tokens and uses the `tid` claim as the managed-link owner.
- PostgreSQL `url_mappings.tenant_id`, repository queries, and cached managed redirect targets carry that Entra tenant ID. Guest links have no owner, expire after seven days, and have no analytics.
- Slice 5 is being developed in a separate worktree and requires tenant-aware managed-click events. Do not change its event contract or source files from this plan. Reconcile its final schema and event version before implementing the account migration.
- Live wrong-scope rejection, second-tenant isolation, and actual seven-day guest expiry remain separate open checks. No deployment is claimed.

## Recommended account model

1. Give every authenticated person an application user ID. Store login identities separately, keyed by validated issuer plus the provider's stable user identifier (`oid` or `sub`, as appropriate for that issuer and token). Treat email and display name as mutable profile/contact fields, never as authorization keys. Link two identities to one application user only after an explicit, authenticated linking flow.
2. Give every user a personal workspace. A managed link belongs to a workspace, not directly to an identity-provider tenant. Personal workspaces have one owner; team workspaces have explicit memberships and an owner/admin/member role model. An organization member may also keep a separate personal workspace.
3. Keep organization membership in the application database. Begin with invitation-only team membership, including invite expiry and single use. Do not grant access solely because an email domain matches or because users share an external customer tenant. Every managed API query and mutation must resolve the selected workspace and verify membership/role server-side.
4. Continue to treat anonymous guest links as unowned. Public redirect lookup may remain anonymous, but management and analytics must be scoped to the verified workspace. Preserve short-code global uniqueness unless a separate URL-routing design changes it.

## Identity approach

- Preserve the existing workforce Entra sign-in and delegated API path during migration. Map each existing Entra `tid` to a legacy team workspace, backfill its managed links, and retain its behavior until membership rules are explicitly transitioned and tested.
- For new customer accounts, evaluate Microsoft Entra External ID in a customer tenant. Its hosted browser sign-up/sign-in flow supports email/password or email one-time passcode and Google federation. Keep passwords and Google OAuth credentials with the identity provider; the application stores only its own user, workspace, membership, and external-identity references.
- Configure the Web and API to accept only explicitly configured issuers, audiences, and delegated scopes. Resolve a validated token to an application user and authorized workspace before calling managed services. A customer tenant's `tid` identifies that identity tenant; it cannot serve as a unique owner for each customer.
- Offer users clear choices to sign in, create an account, or use Google. After sign-in, default to the personal workspace and provide an explicit workspace switcher for team members. Do not silently merge accounts that share an email address.

## Implementation sequence after Slice 5

1. **Decision and threat-model slice:** confirm identity provider, sign-up methods, team roles/invitation policy, account-linking policy, and whether existing Entra tenants retain automatic access during transition. Review Slice 5's final event/outbox schema. Record issuer, scope, account-recovery, and migration rules before editing auth code.
2. **Ownership schema slice:** add application users, external identities, workspaces, memberships, invitations, and `owner_workspace_id` for managed links through explicit migrations. Backfill one team workspace per existing Entra tenant. Keep `tenant_id` during a staged migration; verify row counts and tenant isolation with an isolated database and a rollback plan. Do not clear the application database.
3. **API authorization slice:** map validated identities to users; authorize every managed endpoint by workspace membership and role. Migrate repository queries, redirect analytics, cache metadata, and the final Slice 5 event/aggregate contract with a versioned, backward-compatible transition. Keep guest behavior and public redirects unchanged.
4. **Sign-up and UI slice:** add hosted customer sign-up/sign-in, Google federation, personal workspace onboarding, invitation acceptance, and workspace switching. Preserve the existing Entra sign-in until its users and links have a verified migration path.
5. **Verification slice:** test personal-user separation, team member roles, cross-team denial, same-email identities, explicit identity linking, invite replay/expiry, issuer/audience/scope rejection, existing-tenant migration, guest invariants, and analytics ownership. Distinguish local test handlers from live Entra/Google sign-in and second-account verification before deployment.

## Acceptance and open decisions

- A personal user sees only their own workspace links; a team member sees only explicitly authorized team links. API enforcement does not depend on UI state, email domain, or the customer tenant ID alone.
- Existing managed links remain accessible to their intended organization after migration, with verified counts and no lost history. Slice 5 click events and aggregates remain correctly attributed and idempotent across the ownership transition.
- Email sign-up and Google sign-in create or locate the intended application user without storing passwords in this repository. Account linking requires authenticated proof of both identities.
- Before implementation, decide the exact team roles, invitation policy, whether one person may belong to multiple teams, and whether customers can create teams themselves. Planning assumption: owner/admin/member roles, invitation-only membership, multiple-team membership, and team creation by a signed-in user.

## Source references checked 2026-10-13

- Microsoft Entra External ID customer sign-up and identity providers: https://learn.microsoft.com/en-us/entra/external-id/customers/overview-customers-ciam and https://learn.microsoft.com/en-us/entra/external-id/customers/concept-authentication-methods-customers
- Microsoft access-token claim guidance and claim validation: https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference and https://learn.microsoft.com/en-us/entra/identity-platform/claims-validation
