# Customer local Compose cutover validation — 2026-10-13 12:05 CDT

Branch: `codex/workspace-authorization` at `83dc126`. Purpose: complete the local and portal setup for live customer External ID testing while preserving the workforce configuration and all application data.

## Verified portal state

Using the already authenticated customer-tenant Microsoft Graph session, the Customer API application `8911f6b3-365c-4ce5-a0f7-d12b2e4f230a` reported `api.requestedAccessTokenVersion=2`. The Customer Web application `93f2f816-0a95-41e0-b7df-7ca3c0cce8a0` already had both `https://localhost:63656/signin-oidc` and `https://localhost:63656/signout-callback-oidc` in `web.redirectUris`. No manifest or redirect URI mutation was needed.

The prior Customer Web credential was retained. A separate local Compose credential was added to that Web registration through Microsoft Graph as `schwab-compose-customer-2026-10-13`, key ID `c116c3fb-81b3-404e-8ff4-1c37277fa950`, expiring `2027-04-13T00:00:00Z`. The secret value was written directly to private mode-0600 configuration and was not displayed, logged, or committed. Credential addition followed [Microsoft Graph's addPassword API](https://learn.microsoft.com/en-us/graph/api/application-addpassword?view=graph-rest-1.0).

## Preserved configuration and local cutover

The original private workforce files remain at `/Users/michaelinman/.config/schwab-url-shortener/api.env` and `web.env`. Byte-verified mode-0600 backups of those files and `apps.env` were made with suffix `.backup-2026-10-13-1202-customer-switch` before changing any selector. Separate private mode-0600 `api.customer.env` and `web.customer.env` contain the customer settings; the API file retains the guarded `Schwab_test_local` database connection and Redis settings. Only `SCHWAB_API_ENV_FILE` and `SCHWAB_WEB_ENV_FILE` in private `apps.env` changed to select those customer files. Compose `env_file` uses `format: raw`; the resolved secret was checked for exact equality in memory without outputting it. `docker compose config --quiet` passed.

Only the Schwab API and Web images were rebuilt and their containers recreated with `--no-deps`. No PostgreSQL, Redis, Kafka, Analytics, AI-Influencer-Factory service, network, or volume was recreated or deleted. The existing `Schwab_local` database was not migrated, truncated, or tested.

## Observed result and remaining acceptance

The customer Web `/health/ready` returned 200 and `/health/identity` reported configured. Its own `/account/signin` returned 302 to `schwaburlshortener.ciamlogin.com` with client ID `93f2f816-0a95-41e0-b7df-7ca3c0cce8a0` and callback `https://localhost:63656/signin-oidc`. All six Schwab Compose services reported running and healthy after the cutover. This proves local configuration and challenge initiation, not a completed customer sign-in or successful delegated token exchange.

The operator browser acceptance is pending: register/sign in through `https://localhost:63656`, confirm one personal workspace and repeat sign-in stability, create/list/redirect a managed link, sign out/in and see the same link, then sign in as a second email user and confirm isolation. The first real API access token must have `ver=2.0`; the agent should inspect this only through sanitized claim metadata, never print token material. Google federation and Google sign-in remain a separate operator gate. No production deployment or push occurred.
