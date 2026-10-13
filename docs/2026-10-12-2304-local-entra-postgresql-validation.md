# Local Entra and PostgreSQL validation — 2026-10-12 23:04 CDT

## Purpose and scope

Record the interactive local test of the Slice 3 Web app against the Slice 2 API and a dedicated PostgreSQL 16 container. This document records observed behavior, not deployment or production acceptance. No secret values belong in this file.

## Plan and acceptance criteria

1. Configure the API's client ID and database connection through local user secrets; keep the Web client secret in its existing local user-secrets store.
2. Apply the checked-in migration to a dedicated database and confirm readiness.
3. Sign in through the Web app, perform a managed read and write, follow a public redirect, and confirm the analytics count and detail read.
4. Keep remaining guest, restart persistence, full suite, and cross-tenant checks explicit until exercised.

## Local setup and evidence

- Slice 2 branch `codex/slice-2-postgresql-persistence` contains commits `04358ef` (API user-secrets ID) and `55575cb` (managed `access_as_user` scope policy and focused test). The Web configuration is on Slice 3 branch `codex/slice-3-blazor-operator-ui` at `954c6aa`.
- The API app registration is multitenant. The Web app registration has the API's `access_as_user` delegated permission and home-tenant admin consent, as reported during setup.
- The first Web callback failed with `AADSTS7000215` because the local Web `ClientSecret` setting contained the client ID. The existing local secret store was backed up, then corrected with the saved secret Value through a hidden prompt. No secret value was displayed or committed.
- A dedicated container `schwab-url-shortener-slice2-postgres` uses loopback port `63650`, database `schwab_slice2_local`, and a separate Docker volume. Its generated password is kept in a restricted local file under `~/.config/schwab-url-shortener/`; the unrelated PostgreSQL service on host port `5432` was not changed.
- The EF Core migration `20261013015738_InitialUrlMappings` completed. The API started at `https://localhost:7100`, and `/health/ready` responded `Healthy`.
- The Web app started at `https://localhost:7130`. A real home-tenant Entra sign-in reached `/dashboard`. After the API started, the managed list loaded with no links instead of an API error. Because the API now requires `access_as_user`, this is evidence of an accepted delegated token on the managed read path; the token itself was not inspected or recorded.
- Through the Web UI, a managed link with code `code` and destination `https://example.com/` was created and listed. Its public `/r/code` link redirected to the destination. Refresh showed one redirect and a last-accessed time. The Details panel showed the same destination, count, and last-accessed time.
- In a private browser window without sign-in, the Guest link form loaded. A guest link with code `guestcheck` and destination `https://example.org/` was created and its short URL redirected to the destination, as reported by the user.
- The earlier focused missing/wrong-scope regression test passed with test identities, the solution build passed with zero warnings/errors, and 19 unit tests passed. The focused test did not contact Entra or PostgreSQL.

## Remaining verification

- Guest expiry at the seven-day boundary has not been exercised in this live run; existing automated tests cover the exact boundary.
- The full integration suite needs a separate disposable database named `schwab_slice2_test_*`; those tests truncate `url_mappings` and must not target `schwab_slice2_local`.
- Restart persistence, a live wrong-scope rejection, a second-tenant isolation test, and a tenant onboarding policy have not been verified. Current managed links are tenant-shared; per-user ownership using `oid` is not implemented.
- No branch push, deployment, production credential setup, or operational acceptance is claimed.
