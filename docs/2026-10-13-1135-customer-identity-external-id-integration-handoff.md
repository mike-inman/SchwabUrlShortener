# Customer identity External ID integration handoff — 2026-10-13 11:35 CDT

Status: drafted, not yet implemented. Purpose: complete Microsoft Entra External ID customer sign-up/sign-in, real API token validation, API-owned first-login provisioning, and the Web workspace dashboard, building on the implemented workspace authorization at `cb06ac1`.

You are working in `/Users/michaelinman/Repos/SchwabUrlShortener-workspace-authorization` on branch `codex/workspace-authorization`.

## Repo standards

- Read `AGENTS.md`. Confirm `pwd -P`, branch, commit, worktree list, and `git status --short --branch` before edits. The expected tip is `cb06ac1` with a clean tree; treat any modified or untracked file as user-owned.
- This handoff was drafted in the canonical checkout at `/Users/michaelinman/Repos/SchwabUrlShortener/docs/`. Copy it unchanged into the worktree's `docs/` and commit it there by itself first, then implement on the same branch. Do not commit it on the canonical branch.
- Preserve `.env*`, private Compose env files, local user-secrets, keys, passwords, agent configuration, databases, volumes, containers, the unpublished assignment PDF, and unrelated changes. Never print secret values or run `dotnet user-secrets list`.
- Do not migrate, truncate, or test against `Schwab_local` without explicit operator approval. Use only guarded `Schwab_test_local`.
- Do not change any AI-Influencer-Factory service, network, or volume.

## Read first

- `docs/2026-10-13-0541-personal-and-organization-accounts-plan.md`
- `docs/2026-10-13-0648-accounts-foundation-decisions-and-threat-model.md`
- `docs/2026-10-13-0836-customer-identity-external-id-assessment-plan.md`
- `docs/2026-10-13-0841-workspace-authorization-api-event-validation.md`
- `docs/2026-10-13-0942-full-local-compose-apps-validation.md`
- `compose.yaml`, `compose.https.yaml`, `docker/web.env.example`, `docker/api.env.example`
- `src/Shortener.Api/Program.cs`, `src/Shortener.Web/Program.cs`, and the Web API client, token provider, and dashboard components

## Architecture decision (record as a dated ADR in `docs/` before auth code changes)

- **One identity provider per deployment, selected through configuration.** A deployment runs against either the External ID customer tenant or the existing workforce Entra configuration, never both concurrently. No dual OIDC schemes in the Web app and no dual JWT schemes in the API.
- **The customer tenant is the target for the product.** Personal and organization accounts are both application workspaces with memberships. Neither is derived from an identity-provider tenant, `tid`, or email domain. Organizations are team workspaces a signed-in user creates; membership comes only from explicit application membership or a verified invitation.
- **Workforce support stays available under its existing configuration**, including the legacy tenant routes and V1 analytics path. Do not remove or weaken it.
- **Enterprise SSO is a future External ID federation configuration**, not a second application scheme.
- Record trade-offs: no concurrent workforce and customer sign-in in one deployment; customer-organization members use External ID sign-in until federation is configured for them.

## Operator-confirmed portal state (nonsecret)

- External tenant `schwaburlshortener.onmicrosoft.com`, tenant ID `6adc36e9-dfe5-469f-8aed-6ef83abcdd19`, authority host `https://schwaburlshortener.ciamlogin.com/`.
- Customer Web app `93f2f816-0a95-41e0-b7df-7ca3c0cce8a0`: confidential web client, single tenant. Redirect URI `https://localhost:63656/signin-oidc`. Implicit/hybrid grants disabled, public client flows disabled, native authentication disabled.
- Customer API app `8911f6b3-365c-4ce5-a0f7-d12b2e4f230a`: Application ID URI `api://8911f6b3-365c-4ce5-a0f7-d12b2e4f230a`, delegated scope `access_as_user`. Customer Web has `access_as_user`, `openid`, `offline_access`, and `User.Read`, all with admin consent granted.
- User flow `signup-signin` (email sign-up), associated with Customer Web. Test customer "Mike" exists in the external tenant.
- **Two portal items not yet verified (operator completes in parallel; do not block code work on them):**
  - Customer API manifest `requestedAccessTokenVersion` must be `2`. Its current value has not been read. Because the Customer API is new and has issued no tokens to the application, setting it to `2` affects no existing identity. The agent should confirm the value from the `ver` claim of the first real access token and stop if it is not `2.0`. Do not change the legacy workforce API registration's token version; that could change issuer values for existing identities.
  - Post-logout redirect URI `https://localhost:63656/signout-callback-oidc` must be registered on Customer Web. The last portal check showed only `/signin-oidc`, so treat it as missing until the operator adds it. Confirm the path against the Web app's actual sign-out callback handler. Front-channel logout (`/signout-oidc`) is optional; it supports coordinated sign-out across applications and is not a prerequisite for basic logout.
- **Google federation is not configured yet.** It is operator-owned (Google Cloud OAuth client, External ID identity provider, user-flow association) and runs in parallel. Google sign-in is required for the completed bundle, but email registration/login work must not wait for it.

## Earlier callback error (hypothesis, not a finding)

The operator ran the portal's **Run user flow**, which returned an authorization code to `https://localhost:63656/signin-oidc` and showed the generic Web error page. Working hypothesis: an unsolicited, portal-initiated callback reached a Web app configured for the workforce tenant (no matching state/nonce correlation and a different client). Confirm or refute from sanitized Web logs. Report the exception type and handler stage without logging codes, tokens, cookies, or personal data. If the logs show a different cause, fix that cause; do not assume.

## Configuration and secrets

- Compose reads the private files named by `SCHWAB_WEB_ENV_FILE` and `SCHWAB_API_ENV_FILE`. The Web client secret is saved privately by the operator, but it is **not established that it is installed in the Compose Web env file**. Define the exact variable names and nonsecret values the customer configuration needs (for example `AzureAd__Instance`, `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__ClientSecret`, `ShortenerApi__Scope` for Web, and the API equivalents). Update `docker/*.env.example` with placeholders only. The operator installs the secret; never request, print, or commit it.
- Verify the External ID authority form that `Microsoft.Identity.Web` expects for `ciamlogin.com` against current Microsoft documentation and the tenant's discovery document, rather than assuming.

## In scope

1. **ADR** as described above.
2. **API token validation for the customer tenant:** issuer, audience (`8911f6b3-…`, accepting both the GUID and the `api://` form if required), lifetime, signing keys, and delegated scope `access_as_user`. Reject ID tokens, wrong-audience tokens, and missing or ambiguous subject. Keep the workforce configuration path working when it is the selected provider.
3. **Claim mapping:** the API reads `iss` and `sub` via `FindFirstValue`. ASP.NET Core JWT handlers may map inbound claims (for example `sub` to `ClaimTypes.NameIdentifier`). Determine the actual claim types produced by the configured handler and make issuer/subject resolution correct and explicit. Document the identity key (validated issuer plus `sub`, or `oid` if chosen and justified), note that `sub` is pairwise per resource application, and state that only the API provisions users.
4. **API-owned provisioning:** idempotent, concurrency-safe first-call creation of the application user, external identity, and personal workspace, exposed through an endpoint such as `GET /api/me/workspaces` that returns the caller's accessible workspaces. A repeat or concurrent first call must return the same user and workspace. No email-based matching or merging.
5. **Web dashboard on workspace routes:** discover the caller's workspaces through the API, default to the personal workspace, and call `/api/workspaces/{workspaceId}/...` routes for managed create/read/analytics. Preserve guest behavior, the existing automatic dashboard updates (no Refresh control or polling), and existing authorization behavior.
6. **Sign-in, sign-out, and renew** work against the selected provider. Sign-out returns through the registered post-logout callback.

## Out of scope

- Concurrent workforce and customer schemes; account linking; invitation emails and redemption UI; team creation UI beyond what already exists; enterprise federation.
- Portal or Google Cloud changes (operator-owned).
- `Schwab_local` migration, deployment, push, volume removal, secret rotation.

## Testing

- **Real JWT handler tests, not only the fake authentication handler.** Exercise the actual configured JWT bearer pipeline with locally signed test tokens (test signing key and issuer metadata in tests only) to prove claim mapping, issuer/audience/scope validation, ID-token rejection, wrong-audience rejection, and missing-subject fail-closed behavior. Fake-handler tests remain for authorization logic.
- Real PostgreSQL tests on guarded `Schwab_test_local`: provisioning idempotency and concurrency; two customers sharing the same `tid` cannot see each other's links; same-email identities stay separate; existing workspace and legacy authorization tests stay green.
- Full `dotnet build` and `dotnet test` with real PostgreSQL, Redis, and Kafka where safely available. Report exact counts, warnings, and skips. The current baseline is 60/60.

## Acceptance

Automated (agent): all tests above pass; Compose config validates; Web and API start healthy with customer configuration once the operator installs the secret.

Live (operator performs browser steps; agent records sanitized evidence from logs and database counts):

1. From the Dockerized Web over HTTPS, a new customer registers with email through the app's own Sign in button, not the portal.
2. The customer signs in and receives exactly one personal workspace (verified on a repeat sign-in).
3. The customer creates a managed link, sees it on the dashboard, and the redirect works.
4. The customer signs out, signs back in, and sees the same link.
5. A second customer cannot see the first customer's link.
6. **Google:** once the operator finishes federation, a Google account registers and signs in and receives its own separate user and workspace. Report Google as pending, with the exact remaining operator steps, if it is not configured during this slice.

## Documentation and completion

- Dated ADR, dated validation note (source commit, configuration variable names, evidence, automated versus live results, remaining operator/provider steps), and updated developer setup in `README.md` or `docs/` describing how to supply private configuration separately.
- Review staged paths and diffs; commit only intended files. Clearly distinguish automated tests from live identity verification. Do not claim deployment or production readiness.
