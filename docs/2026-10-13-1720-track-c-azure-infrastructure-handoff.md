## Track C — Azure Staging Infrastructure Handoff
Status: drafted on 2026-10-13
You are working in /Users/michaelinman/Repos/SchwabUrlShortener-infra.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Create the worktree first, from the main checkout: `git -C /Users/michaelinman/Repos/SchwabUrlShortener worktree add /Users/michaelinman/Repos/SchwabUrlShortener-infra -b codex/azure-infra origin/main`. Confirm `origin/main` is `66b8318a351788d6cec5ba6e3bfa3f0d0ed63ba0`; if it moved, use the new tip and record it.
- Copy this handoff and `docs/2026-10-13-1720-azure-scaleout-contract.md` from `/Users/michaelinman/Repos/SchwabUrlShortener/docs/` into the new worktree's `docs/` and commit them first as `docs: add Track C handoff and Azure scale-out contract`.
- Preserve all other worktrees. Do not edit, reset, stash, or check out anything outside this worktree.
- Edit only `infra/**`, `azure.yaml`, and your dated docs. Never edit `src/`, `tests/`, `Dockerfile`, or any `compose*.yaml`.
- Do not modify `.env*`, credentials, local agent configuration, the assignment PDF, or any local Docker container or volume.
- The contract file is authoritative for names, network ranges, settings, and the Event Hubs topic. Do not diverge; stop and report if it is wrong.

Deployment authority:
- You may create and update resources only in resource group `rg-shortener-staging`, in the subscription selected by the operator's current `az account show`. Print the subscription name and ID before the first deployment and stop if it is not what the operator confirmed in chat.
- Run `az deployment group what-if` (or `azd provision --preview`) before every provisioning run and include its summary in the validation note.
- Never delete the resource group, a database, Key Vault, or storage account. Never purge Key Vault. Never touch resources outside `rg-shortener-staging`.
- If `az login` / `azd auth login` are unavailable or lack permission, complete authoring and offline validation, then stop and report exactly which role or login is missing.

Task: Author and deploy, in four gated phases, the Azure staging environment that runs the existing API, Web, and Analytics container images with managed PostgreSQL, Redis, Event Hubs, and Front Door.

Goal:
A reproducible `azd up` against `rg-shortener-staging` creates a private, identity-based staging environment. Its first deployment runs today's `main` images with one replica each and passes a guest create-and-redirect smoke test through Front Door. It is ready to scale API and Web to two or more replicas as soon as Track A merges, and to run Analytics against Event Hubs.

Important context:
- The repo builds all images from one root `Dockerfile` with targets `api`, `web`, `analytics` and build arg `PROJECT` (see `compose.yaml`). Images listen on port 8080.
- Public routing must mirror `deploy/Caddyfile.local`: `/api/*`, `/r/*`, `/health/*` to API; everything else to Web.
- Migrations run with the API image and argument `--migrate`, once per release, never from app replicas.
- Track A (in parallel) adds: shared Data Protection keys (Blob + Key Vault), a distributed Redis token cache, network-range proxy trust, SASL_SSL Kafka settings, and Analytics HTTP health endpoints on 8080. Until Track A merges, today's images cannot use those settings. That shapes the first deployment below.
- Track B (in parallel) adds Redis pub/sub live updates using `LiveUpdates__*` settings.
- Blazor Server requires sticky sessions with multiple Web replicas. In Container Apps, session affinity requires single revision mode and HTTP ingress.
- Azure Cache for Redis is being retired; use Azure Managed Redis, which is clustered by default.
- Event Hubs Standard fixes partition count at creation; the contract sets 32.

In scope only:

C1 — Foundation
1. `infra/main.bicep` with modules under `infra/modules/`, parameters in `infra/main.staging.bicepparam`, `azure.yaml` for `azd` with services `api`, `web`, `analytics` in prebuilt-image form, and `infra/scripts/build-images.sh`, all exactly as the contract's "Image build" section describes.
2. Virtual network and subnets exactly per the contract.
3. Log Analytics workspace and workspace-based Application Insights.
4. Azure Container Registry, Standard SKU, admin user disabled.
5. Key Vault, RBAC authorization, soft delete and purge protection enabled, public network access disabled, private endpoint.
6. User-assigned managed identities: `id-shortener-api-stg`, `id-shortener-web-stg`, `id-shortener-analytics-stg`. Each gets `AcrPull` on the registry and `Key Vault Secrets User` on the vault.
7. A resource group budget of USD 300 per month with alerts at 80% and 100% to the email in `azd` environment value `BUDGET_CONTACT_EMAIL`. If that value is unset, omit the budget and record it as an operator action.

C2 — Data services, all with public network access disabled and private endpoints in `10.60.4.0/24`, plus private DNS zones linked to the VNet:
8. PostgreSQL Flexible Server: version 17, General Purpose `Standard_D2ds_v5`, 64 GiB storage, 7-day backups, zone redundancy off for staging, built-in PgBouncer enabled in transaction pool mode, database `shortener`. Administrator login `shortener`, password from `azd` secret environment value `POSTGRES_PASSWORD`, never committed. Write Key Vault secrets `pg-app-connection` (port 6432) and `pg-migrate-connection` (port 5432) exactly per the contract.
9. Azure Managed Redis: smallest Balanced SKU available in the region (`Balanced_B0`, else `Balanced_B1`), OSS cluster policy, TLS on port 10000, access-key authentication. Write Key Vault secret `redis-connection` per the contract.
10. Event Hubs: Standard namespace, 1 throughput unit with auto-inflate to 4, event hub `managed-clicks-v2` with 32 partitions and 7-day retention, consumer group `shortener-analytics-v1`, a namespace authorization rule with Send and Listen only. Write Key Vault secret `eventhubs-connection`. Confirm in Microsoft documentation that Standard supports private endpoints; if not, keep public access limited to the Container Apps environment's static outbound IP and record why.
11. Storage account for Data Protection keys: StorageV2, Standard_LRS, shared-key access disabled, blob container `dataprotection`. Key Vault keys `dataprotection-api` and `dataprotection-web` (RSA 2048). Grant `id-shortener-api-stg` and `id-shortener-web-stg` `Storage Blob Data Contributor` on the container and `Key Vault Crypto User` on their own key only.

C3 — Container Apps
12. Container Apps environment with workload profiles (Consumption profile), VNet-integrated into `10.60.0.0/23`, external ingress, logging to the Log Analytics workspace.
13. `ca-shortener-api-stg`: external HTTP ingress on 8080; liveness `/health/live`, readiness `/health/ready`; HTTP scale rule at 50 concurrent requests. Environment and Key Vault secret references exactly per the contract for the API.
14. `ca-shortener-web-stg`: external HTTP ingress on 8080, single revision mode, session affinity `sticky`; liveness `/health/live`, readiness `/health/ready`; HTTP scale rule at 50 concurrent requests. `ShortenerApi__BaseUrl` = `http://ca-shortener-api-stg/` with `ShortenerApi__AllowedInternalHttpHosts` = `ca-shortener-api-stg`, per the contract. Verify the in-environment HTTP call succeeds; if Container Apps redirects it to HTTPS, set `allowInsecure: true` on the API ingress only.
15. `ca-shortener-analytics-stg`: no ingress; KEDA `kafka` scale rule on consumer group `shortener-analytics-v1`, topic `managed-clicks-v2`, lag threshold 100, SASL plain over TLS using secrets from Key Vault; minimum 1, maximum 32 replicas.
16. `caj-shortener-migrate-stg`: Container Apps Job, manual trigger, API image with args `--migrate`, using `pg-migrate-connection`, API identity, 1 parallelism, 0 retries, 10-minute timeout.
17. Replica counts are parameters, and the first deployment uses:
    - API: min 1, max 1. Web: min 1, max 1. Analytics: min 0, max 0 (deployed but not running).
    - After Track A merges, the target values are API min 2 / max 10, Web min 2 / max 6, Analytics min 1 / max 32. Put both sets in `main.staging.bicepparam` with the first-deploy set active and the scale-out set commented, labeled with the Track A merge as its precondition.
18. Settings that only Track A's code understands (`DataProtection__*`, `TokenCache__*`, `ForwardedHeaders__KnownNetworks`, `ForwardedHeaders__ForwardLimit`, `Kafka__Sasl*`, `Kafka__SecurityProtocol`, `Kafka__EnableIdempotence`) and Track B's (`LiveUpdates__*`) are defined in Bicep behind one boolean parameter `enableScaleOutSettings`, default `false` for the first deployment. With it `false`, apps receive only settings today's images use. Set `Kafka__EnableIdempotence` to the value Track A reports; until then use `false` and record it.

C4 — Edge
19. Azure Front Door Standard profile with one endpoint. Origin group `api` (origin: API app FQDN) and origin group `web` (origin: Web app FQDN), each with its health probe on `/health/live`. Routes: `/api/*`, `/r/*`, `/health/*` to `api`; `/*` to `web`. HTTPS only with HTTP redirect, caching disabled, origin host header set to the origin FQDN.
20. Custom domain from `azd` environment value `CUSTOM_DOMAIN` with Front Door managed certificate when set. When unset, use the `*.azurefd.net` endpoint host as the public host, and record that Michael must supply the domain and DNS records.
21. Set `PublicLinks__BaseUrl` and `AllowedHosts` from the resolved public host, per the contract. This creates a dependency from apps to Front Door; handle it without a cycle, for example with a second deployment pass or deterministic endpoint naming, and document the approach.

Out of scope:
- Any change under `src/`, `tests/`, `Dockerfile`, or Compose files.
- Production environment, multi-region, zone redundancy, Front Door Premium with Private Link, WAF policy tuning, Azure SignalR Service, PostgreSQL Entra authentication, Event Hubs OAuth. Record each as a production follow-up.
- Entra app registration changes (Michael performs those).
- Load or failure testing (Track E).

Required discovery before edits:
- Read the contract in full, `compose.yaml`, `compose.local.yaml`, `compose.cloud.yaml`, `deploy/*.example`, `deploy/Caddyfile.*`, `Dockerfile`, `README.md`, and `docs/2026-10-13-0734-kubernetes-readiness-plan.md`.
- Check current Microsoft documentation, and record the URLs, for:
  - SKU availability in `centralus` for every resource above; fall back to `eastus2` per the contract if needed.
  - Built-in PgBouncer support for the chosen PostgreSQL tier.
  - Azure Managed Redis Bicep resource type, API version, SKU names, and private endpoint support.
  - Event Hubs Standard private endpoint support.
  - Front Door Standard WebSocket support. Blazor Server needs WebSockets end to end; if Front Door Standard does not support them, stop before C4 and report options.
  - KEDA Kafka scaler settings for Event Hubs SASL in Container Apps.
  - `az acr build` support for `--target` and `--build-arg`, and `azd` prebuilt `image:` with environment substitution and `predeploy` hooks (the `azure.yaml` schema has no Docker target setting; see the contract).
- Inspect installed versions of `az`, the Bicep CLI, and `azd`.

Implementation requirements:
- No secret value appears in any committed file, deployment output, or log. Secrets flow only through Key Vault and Container Apps Key Vault references using the app's managed identity.
- Every app sets `AZURE_CLIENT_ID` to its own identity's client ID.
- Least privilege: each identity can read only the secrets and keys its app uses.
- Bicep lints with zero errors and zero warnings, or each remaining warning is listed with a reason.
- Images are deployed by digest, not by mutable tag.
- Resource names follow the contract's naming convention.

Exception / observability requirements:
- All apps send console logs to Log Analytics. Record the query that shows each app's structured "Service started" event.
- Set `APPLICATIONINSIGHTS_CONNECTION_STRING` on all apps for later OpenTelemetry export; exporting is not in this track.
- Diagnostic settings send PostgreSQL, Redis, Event Hubs, Key Vault, and Front Door logs and metrics to the Log Analytics workspace.

Realtime / no-polling requirements:
- Not applicable to infrastructure. Health probes are bounded platform probes. Verify Front Door forwards WebSocket upgrades to Web, because Blazor's live connection depends on it.

Testing, in order, each phase gated on the previous one:
- Offline: `az bicep build --file infra/main.bicep` and `az bicep lint` pass; `azd provision --preview` or `what-if` output recorded.
- C1–C2: deploy; confirm in the portal or CLI that every data service has public access disabled and resolves to a private IP from the VNet; confirm Key Vault secrets exist (names only, never values).
- C3: build and push today's images with `infra/scripts/build-images.sh` (via `azd deploy`'s `predeploy` hook); run the migration job once and record its exit status; confirm API and Web replicas are healthy; Analytics is at 0 replicas.
- C4: through the public host:
  - `GET /` returns 200 from Web.
  - `GET /health/ready` returns 200 from API.
  - Guest create returns 201 and its `/r/<code>` returns 302.
  - A WebSocket upgrade to the Web Blazor endpoint succeeds through Front Door.
- Record what was not exercised: sign-in (needs Track A's forwarded-header trust and Michael's redirect URIs), Analytics against Event Hubs (needs Track A's SASL settings), and multi-replica behavior.

Documentation:
- Add `docs/2026-10-13-<HHMM>-track-c-azure-infrastructure-validation.md` with phase-by-phase commands, what-if summaries, resource inventory, documentation URLs checked, smoke results, an estimated monthly staging cost from the Azure pricing calculator or `az` cost data, and the remaining operator actions.
- Add a short `infra/README.md` describing `azd env` values (`POSTGRES_PASSWORD`, `BUDGET_CONTACT_EMAIL`, `CUSTOM_DOMAIN`), the two replica sets, `enableScaleOutSettings`, and how to run the migration job.
- Update the root README surgically with a link to `infra/README.md`.

Completion criteria:
- [ ] C1–C4 authored as Bicep modules with `azure.yaml`; offline validation passes.
- [ ] Staging deployed in `rg-shortener-staging` with private data services, managed identities, and Key Vault-referenced secrets only.
- [ ] Migration job ran once successfully.
- [ ] Today's images serve the guest flow through Front Door, including a WebSocket upgrade to Web.
- [ ] The scale-out replica set and `enableScaleOutSettings` are ready to switch on after Track A merges, with no infrastructure rework.
- [ ] No secret committed or logged; only `infra/**`, `azure.yaml`, and dated docs changed.
- [ ] Remaining operator actions and production follow-ups are listed.

Report back: final branch commit, the public host, smoke results, estimated monthly cost, and the exact operator actions still needed.
