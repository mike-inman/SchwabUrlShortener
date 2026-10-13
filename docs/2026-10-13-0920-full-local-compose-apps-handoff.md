## Complete Local Compose Application Bundle Handoff
Status: drafted on 2026-10-13
Drafted at 09:20 CDT
You are working in /Users/michaelinman/Repos/SchwabUrlShortener-workspace-authorization.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm physical path, branch `codex/workspace-authorization`, commit `0be8b3f`, `origin/main`, worktree list, and `git status --short --branch` before edits. Preserve current changes; never reset or overwrite work.
- Preserve `.env` files, API keys, passwords, local agent configuration, the unpublished assignment PDF, all databases and volumes, and unrelated changes. Do not touch AI-Influencer-Factory Docker services.
- Keep the assignment within the scope below. Use narrow validation after the first substantive edit.

Task: Add the API, Blazor Web app, Analytics worker, and explicit database migration step to the existing Schwab Docker Compose project, then verify a reproducible local application stack.

Goal:
Docker Desktop shows one `schwab-url-shortener` group containing PostgreSQL, Redis, Kafka, API, Web, and Analytics. A developer can build and start the application locally from committed instructions plus their own uncommitted credentials, and run a real guest create/redirect flow. Managed login must use real provider configuration; no fake production identity is allowed.

Important context:
- Commit `0be8b3f` added `compose.yaml` and grouped the three existing data services. They are currently healthy, using original data volumes and loopback ports 63651–63653. Their host-specific volume mappings and the path to the existing PostgreSQL password file are in private mode-0600 `/Users/michaelinman/.config/schwab-url-shortener/compose.env`. Never print its contents or commit it. The existing password file remains untouched.
- Docker exhausted all predefined subnet pools on this host. `compose.yaml` temporarily uses `network_mode: bridge`; Docker Desktop groups services by Compose labels, but the default bridge does not give new app containers reliable service-name DNS. Design a Schwab-only network with an explicit, configurable nonconflicting subnet after inspecting actual Docker and host routes, or another verified service-discovery arrangement. Do not remove AIF networks to free space.
- Existing Kafka advertises `localhost:63653` for host clients. App containers need a separate internal listener/address while preserving host clients, Kafka data, and topic compatibility. The current health check only tests the local listener; host `kcat` metadata succeeded after grouping.
- The current Web app is Blazor Interactive Server and uses a delegated API token. API startup requires a real Entra API client ID; Web's managed sign-in requires its own real provider configuration. See `docs/2026-10-13-0836-customer-identity-external-id-assessment-plan.md` for the required later customer External ID and Google onboarding phase. Do not call a guest-only app a completed customer registration flow.
- The workspace authorization task completed at `5089663` with 60/60 local tests. No application database migration, live customer identity, or deployment is claimed by that result. The current Compose grouping is infrastructure only, as recorded in `docs/2026-10-13-0903-schwab-compose-grouping-validation.md`.

In scope only:
1. Add self-contained multi-stage .NET 10 container builds for `Shortener.Api`, `Shortener.Web`, and `Shortener.Analytics`. Keep images free of source secrets and host paths. Use the existing root `.dockerignore` and preserve the project dependency graph.
2. Extend the existing `compose.yaml` with API, Web, Analytics, and an explicit one-shot migration operation. Provide safe per-developer configuration templates and startup documentation. Reuse existing PostgreSQL, Redis, Kafka volumes and ports during this host's transition. Do not couple schema changes to API startup.
3. Provide container-to-container connectivity, including Kafka internal and host listener addresses, without changing unrelated Docker networks or volumes. Make the network subnet configurable if this host requires explicit IPAM.
4. Configure readiness and dependency failure behavior. API and Web must have useful health/availability checks; Analytics must fail visibly when PostgreSQL or Kafka is unavailable. Keep logs sanitized. Support local HTTPS/OIDC callback setup for real managed login, with secrets and certificates supplied locally and never committed.
5. Verify from a fresh Compose build: real services healthy, migrations only on a guarded disposable Schwab database first, API/Web/worker startup, guest creation and redirect, and analytics publish/consume with real Kafka when configured. Document each live Entra/External ID/Google step that still needs operator action.

Out of scope:
- No automatic migration, truncate, or reset of `Schwab_local` or any application database. Do not apply migrations there without a separately reviewable backup and explicit operator step.
- No Google OAuth or Entra tenant mutation in this container slice; their configuration and complete customer sign-up flow remain required delivery gates in the identity/onboarding work.
- No Kubernetes cluster or Terraform apply, no branch push/merge, no Docker volume prune, no `docker compose down -v`, and no AIF or other service changes.

Required discovery before edits:
- Inspect this handoff, `AGENTS.md`, `compose.yaml`, `.dockerignore`, README, dated Compose grouping plan/validation, API/Web/Analytics project files and startup code, PostgreSQL migrations, Kafka options, current private config key names without values, container IDs/volumes/ports/health, and the repository status.
- Inspect available Docker subnets and host routes before selecting a Schwab-only network. Check current Kafka advertised listeners and preserved topic metadata. Confirm any target file is not concurrently modified.

Implementation requirements:
- Use consistent `schwab-url-shortener-*` service/container names and a single Compose project. Keep data volumes explicit and persistent; never remove or recreate them blindly. Preserve host loopback port boundaries and current developer databases.
- Never bake access tokens, connection strings, client secrets, certificates, or passwords into images, Compose source, logs, or docs. Use ignored local files/secret stores, with placeholders in tracked templates. Existing credentials may be read locally only as needed without displaying values.
- A missing required API identity setting or database connection must fail closed. Guest UI availability is distinct from functioning customer sign-in. No test-only authentication handler in runtime images.
- The migration job must be explicit, idempotent, and separately invoked; test it against only a disposable database before any application database step. Do not let container restarts apply migrations implicitly.
- Preserve V1 and V2 click event handling, outbox durability, and cache fallback behavior from the current branch.

Exception / observability requirements:
- Health and startup failures must identify the failing service and safe category without exposing secrets. Keep existing request/click correlation. Broker and database unavailability must not be reported as successful analytics.

Realtime / no-polling requirements:
- Preserve the Blazor Interactive Server UI's automatic updates and working WebSocket path through the local host endpoint. Do not add manual Refresh controls or polling to mask service failures.

Testing:
- Validate Compose syntax, build three application images, run the Schwab group and verify individual health. Check original volume IDs and PostgreSQL database names before/after; verify host and internal Kafka metadata. Do not touch AIF services.
- Run focused guarded real-service tests and `dotnet build SchwabUrlShortener.slnx` plus `dotnet test SchwabUrlShortener.slnx` where applicable. Record exact passes, warnings, skipped tests, and environment. Distinguish container health, guest end-to-end, analytics, live Entra login, and External ID/Google registration.

Documentation:
- Update README with a fresh-machine Compose setup, secret/certificate preparation, explicit migrations, start/stop commands, backup/restore, and expected service URLs. Add dated plan and validation notes under `docs/`. Keep historic documents accurate. Review staged paths and secret exposure before committing only intended files.

Completion criteria:
- [ ] Docker Desktop shows API, Web, Analytics, PostgreSQL, Redis, and Kafka under one `schwab-url-shortener` project; real app images start and communicate through verified addresses.
- [ ] Guest create/redirect and managed analytics paths are validated against real local services, with exact auth limitations stated. No customer self-registration or Google sign-in is claimed until the separate provider/onboarding phase passes live checks.
- [ ] Existing databases, volumes, credentials, AIF services, and user work remain intact; migrations are explicit and tested on a disposable database first.
- [ ] Dated setup/validation, clean intended-file commit, and a clear list of remaining customer identity and Kubernetes/Terraform gates are delivered.
