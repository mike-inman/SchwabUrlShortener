# Customer identity assessment plan — 2026-10-13 08:36 CDT

## Purpose and decision

Assess a later self-service customer identity phase for SchwabUrlShortener. Microsoft Entra External ID in a dedicated **external tenant** appears appropriate for customer registration, sign-in, and Google federation: its customer user flows support self-service sign-up and social providers. This is a design recommendation, not a configured tenant or verified sign-in. Keep the current workforce Entra delegated API path until a separate live-provider cutover is proven. Provider configuration, onboarding, invitations, and UI are outside the current workspace API/event slice.

## Account model

An application user represents one person. A personal account owns one personal workspace. An organization account is a team workspace containing individual users with explicit owner, admin, or member roles. The application establishes and revokes membership. Neither a shared email/domain nor an identity-provider tenant establishes team membership. A Google-federated customer should be identified by the validated External ID issuer and stable subject, not by email or the upstream Google email address. Linking a second identity to an existing application user requires a separate, verified account-linking flow.

## Later implementation sequence

1. **Tenant choice and prerequisites.** Confirm the external tenant's availability, ownership, region/commercial terms, administrator roles, branding, and whether customer identity should be separated from the current workforce tenant. Keep the existing Entra API registration and live settings intact during evaluation. Record nonsecret tenant IDs and endpoints only after verifying them with the operator.
2. **App registrations.** Register the customer-facing web app and API in the external tenant, set exact redirect/logout URIs, expose a delegated API scope, grant the web app that permission, and check consent requirements. Use authorization code flow with PKCE and server-side secret storage where applicable. Configure a distinct issuer/audience/scheme or policy for customer tokens while preserving the current workforce issuer/audience/scope path.
3. **User flow.** Create a customer sign-up/sign-in user flow, choose the initial local sign-in method and minimal profile attributes, associate the customer web app with it, and test the flow in a browser. Avoid collecting workspace role, membership, or organization identity as an authority-bearing sign-up attribute.
4. **Google sign-in.** Create the Google OAuth application, configure the exact External ID federation redirect URI and consent screen, connect Google as an identity provider in the external tenant, and add it to the user flow. Store the Google client secret only in the provider's secure configuration; never paste it into chat, logs, docs, or source control. Confirm Google testing/publishing requirements with the operator before enabling public use.
5. **Token validation.** Validate the external tenant's discovery metadata, signing keys, issuer, API audience, lifetime, and delegated scope. Reject ID tokens at the API, tokens from the workforce API audience, missing/ambiguous subject, and provider-tenant IDs used as workspace authority. Live-test both accepted customer and rejected workforce/wrong-audience tokens without weakening the existing legacy route. Map the validated External ID issuer plus stable subject to `external_identities`; do not infer an application user from email.
6. **Account provisioning.** After a successful validated first sign-in, transactionally and idempotently create the application user, external-identity row, and personal workspace; handle concurrent first requests and retries through uniqueness constraints. Existing identity lookup should return the same user. Create or join a team only through an explicit application action and verified invitation or membership management flow. Plan recovery for partial provisioning and account-linking collisions without merging users by email.
7. **Customer UI and acceptance.** Add sign-up/sign-in, workspace selection, personal/team onboarding, invitation acceptance, and accessible error states in a later slice. Exercise Google, local sign-in, logout, token refresh, two same-email identities, cross-workspace denial, removed membership, and legacy workforce compatibility with real provider tokens before cutover.

## Step-by-step portal walkthrough with the operator

The operator and I must walk through external tenant creation/selection, role and billing checks, web/API registrations and redirect URIs, API scope and consent, user-flow creation/association, and Google Cloud OAuth consent/client setup plus federation. Each portal step should be verified before the next because values and permissions are tenant-specific and may change. I will ask only for nonsecret identifiers, exact portal status, and test outcomes; secret values remain in the portal or local secret store. No portal change is authorized by this plan alone.

## Acceptance and recovery gates

- A real customer token reaches the intended customer API policy, while wrong issuer, audience, scope, and ID-token cases fail; the legacy Entra path still works.
- A verified individual receives exactly one application user and personal workspace on retry; two identities with the same email remain separate unless explicitly linked.
- Team access follows only current application membership, including revocation, across API reads and writes.
- Before any live cutover, back up application data, rehearse restore, count-check identity/workspace mappings, and prepare a forward repair. Keep provider configuration rollback separate from database recovery.

## Source basis checked 2026-10-13

- [External tenant customer identity overview](https://learn.microsoft.com/en-us/entra/external-id/customers/overview-customers-ciam)
- [Self-service sign-up](https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-overview)
- [Google federation for external tenants](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-google-federation-customers)
- [Register an application](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-register-ciam-app)
- [Associate an application with a user flow](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-user-flow-add-application)
- [Test a customer user flow](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-test-user-flows)
