# Azure scale-out contract — 2026-10-13 17:20 CDT

Status: agreed baseline for Tracks A, B, and C. Revised 2026-10-13 17:50 CDT: Kafka topic corrected to `managed-clicks-v2`, internal API setting corrected to `ShortenerApi__AllowedInternalHttpHosts`, image build method added. Revised 17:58 CDT: Azure account, subscription, and tenant recorded. Revised 18:10 CDT: origin FQDNs added to `AllowedHosts`. **Revised 18:50 CDT: replaced the staging design with one lean, scalable environment** (no staging, no Front Door, no private endpoints, Burstable PostgreSQL, Redis as a container, `rg-schwab-url-shortener`). Michael approved deleting `rg-shortener-staging`; it was deleted at 18:49 CDT. **Revised 19:00 CDT: restored low-cost design controls** — PostgreSQL VNet integration (no public endpoint), Redis persistence, monitoring alerts and diagnostics, and an automated Kafka-lag scale demonstration. **Revised 19:05 CDT**: PostgreSQL pool size set to 2 for the 35-connection B1ms limit. **Revised 19:10 CDT**: design principles added; Event Hubs auto-inflate removed in favor of a fixed 1 throughput unit plus a throughput alert. **Revised 19:15 CDT**: Entra identity settings added, with the Web client secret stored in Key Vault; secret-handling rules stated. **Revised 19:20 CDT**: one managed identity per service, with per-secret Key Vault access. Change this file only by editing it and telling every active track.

## Purpose

Three tracks run in parallel from `origin/main` at `66b8318a351788d6cec5ba6e3bfa3f0d0ed63ba0`:

| Track | Handoff | Branch | Worktree |
|---|---|---|---|
| A — scale-out readiness | `2026-10-13-1720-track-a-scaleout-readiness-handoff.md` | `codex/azure-scaleout-readiness` | `/Users/michaelinman/Repos/SchwabUrlShortener-scaleout` |
| B — dashboard live push | `2026-10-13-1720-track-b-dashboard-live-push-handoff.md` | `codex/dashboard-live-push` | `/Users/michaelinman/Repos/SchwabUrlShortener-livepush` |
| C — Azure infrastructure | `2026-10-13-1720-track-c-azure-infrastructure-handoff.md` | `codex/azure-infra` | `/Users/michaelinman/Repos/SchwabUrlShortener-infra` |

This contract fixes the names and values the tracks share. The application is modified, not rewritten. Local Compose remains a supported way to run the whole system.

## Goal of the Azure environment

There is exactly one Azure environment. It must:

1. Work reliably while evaluators use it: guest links, redirects, signed-in dashboard, live updates.
2. Cost little: small, consumption-billed, scale-to-small when idle.
3. Visibly demonstrate how the system scales: real autoscale rules on every app, multiple replicas behind sticky sessions, Kafka-lag scaling, and a repeatable scale demonstration with recorded replica counts.
4. Make the production upgrade path explicit: each lean choice names the one change that scales it further.

## Design principles

These govern every infrastructure choice and every future revision. A change that violates one must say so and why.

1. **Expected load is low.** Size for it, not for imagined peaks.
2. **Elastic, not idle-expensive.** Every component that can scale out under load must also retract when load falls, without manual action. Capacity that only grows (for example Event Hubs auto-inflate) is avoided or made an explicit, alerted, deliberate step.
3. **Cost-conscious without sacrificing security.** Prefer free or near-free controls that keep the principle intact: private network access, service endpoints with subnet-only firewalls, managed identities, Key Vault, TLS everywhere, least-privilege roles. Paid controls (private endpoints, WAF) are documented production steps with their cost.
4. **Cost-conscious without sacrificing performance.** No cold starts on user-facing paths (minimum 1 replica). Hard limits (database connections, throughput units) are sized with headroom and have an alert before they are reached.
5. **Every lean choice names its upgrade.** For each small or single-instance component, document the one change that scales it further and its approximate cost.
6. **Demonstrable.** Scaling out and back in is shown with a repeatable, bounded script and recorded replica counts, not only described.

## Environment

- Azure account: `social@mediaraq.com`. All Azure work for this project uses this login.
- Subscription: `Azure subscription 1`, ID `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, tenant `mediaraq com` (`eea269ab-1571-48a5-a18c-824ba5859cd5`). Confirmed by Michael on 2026-10-13. Before any deployment, `az account show` must report this subscription ID; if not, run `az account set --subscription baaf4acd-ff40-43d1-a7ba-6089a2c4f41e` and recheck. The same login can see two subscriptions in tenant `3d9ba6ab-7f64-43fa-bf44-346b3c2a3fb6`; never deploy there.
- This subscription is an Azure **free trial** (`FreeTrial_2014-09-01`, spending limit on). Michael must upgrade it to Pay-As-You-Go before the trial ends or credit runs out, or Azure disables the resources. Track C records the trial end date in its validation note.
- Do not modify or reuse resources in `rg-schwab-url-shortener-identity` or any other existing resource group (`rg-agentengineering-*`, `AzureLeadInterviewLab-rg`, `rg-mynetapp-dev`, `NetworkWatcherRG`). `rg-shortener-staging` is being deleted; never recreate it.
- Region: `centralus`.
- Resource group: `rg-schwab-url-shortener`.
- `azd` environment name: `prod`.
- Naming: `<type>-shortener-<service>`, for example `ca-shortener-api`. Globally unique names (storage, Key Vault, registry, Event Hubs, PostgreSQL) append `uniqueString(resourceGroup().id)` truncated to 6 characters.
- Schwab-only boundary: nothing references, reuses, or peers with any AI-Influencer-Factory or other project resource.

## Sizing and scaling

| Component | Lean size | Scaling demonstrated | Production step (documented, not built) |
|---|---|---|---|
| Container Apps environment | Consumption workload profile, VNet-integrated | — | Dedicated workload profiles if needed |
| `ca-shortener-api` | 0.25 vCPU / 0.5 GiB, min 1, max 5 | HTTP rule: 10 concurrent requests per replica | Raise max; higher concurrency target |
| `ca-shortener-web` | 0.25 vCPU / 0.5 GiB, min 1, max 3, sticky sessions | HTTP rule: 10 concurrent requests per replica | Azure SignalR Service for many circuits |
| `ca-shortener-analytics` | 0.25 vCPU / 0.5 GiB, min 1, max 4 | KEDA Kafka lag rule, lag threshold 50; demonstrated live by the scale script through anonymous redirects of one managed demo link | Max up to 32 (partition count) |
| `ca-shortener-redis` | `redis:7.4-alpine`, 0.25 vCPU / 0.5 GiB, min 1, max 1, internal TCP ingress, password required, append-only persistence (`appendfsync everysec`) on an Azure Files share | — | Azure Managed Redis, one connection-string change |
| `ca-shortener-proxy` (only if native path routing is not used; see Public routing) | `caddy:2.10-alpine`, 0.25 vCPU / 0.5 GiB, min 1, max 2 | HTTP rule | Front Door Standard/Premium |
| `caj-shortener-migrate` | Manual job, API image, `--migrate` | — | — |
| PostgreSQL Flexible Server | Burstable `Standard_B1ms`, PostgreSQL 17, 32 GiB, 7-day backups, **private access (VNet integration), no public endpoint**, no PgBouncer | Connection budget sized to max replicas (below) | General Purpose tier plus built-in PgBouncer; zone-redundant high availability |
| Event Hubs | Standard, **fixed 1 throughput unit, auto-inflate disabled** (it only grows and never retracts), hub `managed-clicks-v2` with 32 partitions, 7-day retention, consumer group `shortener-analytics-v1` | Partitions already allow 32 consumers; an alert fires when ingress exceeds 70% of one unit for 10 minutes | Raise throughput units deliberately when the alert shows sustained need |
| Container Registry | Basic | — | — |
| Key Vault | Standard, RBAC | — | Private endpoint |
| Storage (Data Protection keys) | StorageV2, Standard_LRS, shared-key access disabled | — | Private endpoint |
| Storage (Redis persistence) | Separate StorageV2 account, Standard_LRS, Azure Files share `redis-data` (1 GiB quota). Shared-key access is enabled on this account only, because Container Apps Azure Files mounts require the account key. | — | Not needed with Azure Managed Redis |
| Log Analytics | Pay-as-you-go with a 1 GB/day ingestion cap | — | Remove cap, add alerts |
| Budget | USD 100/month, alerts at 50/80/100% to `BUDGET_CONTACT_EMAIL` | — | — |
| Monitoring | Diagnostic settings from PostgreSQL, Event Hubs, Key Vault, and the Container Apps environment to Log Analytics. Azure Monitor alerts, emailed to `BUDGET_CONTACT_EMAIL` through one action group: API or Web 5xx responses above 5% for 5 minutes; any app restarting repeatedly; Analytics consumer lag above 1,000 for 10 minutes; outbox oldest unpublished event above 5 minutes (from the existing `shortener.clicks.outbox.oldest_seconds` metric if exported, otherwise from logs); PostgreSQL CPU above 80% or connections above 80% of `max_connections` for 10 minutes; Event Hubs ingress above 70% of one throughput unit (about 0.7 MB/s or 700 events/s) for 10 minutes. | Alerts show when scaling limits are reached | Paging, dashboards |

Minimum replicas are 1 (not 0) on API, Web, and Analytics so evaluators never wait for a cold start and click analytics stay prompt.

**PostgreSQL connection budget:** Microsoft lists `Standard_B1ms` at 35 user connections. App connection strings set `Maximum Pool Size=2`. Budget at maximum scale: API 5 replicas × 2 = 10; Analytics 4 replicas × (2 pooled + 1 for its readiness check, which uses a separate connection string and therefore a separate pool) = 12; migration job 1; total 23 of 35, leaving 12 for administration and headroom. Web does not connect to PostgreSQL. Track C confirms the server's actual `max_connections` and records the arithmetic. Each query takes milliseconds, so two connections per replica serve far more requests than the scale demonstration sends; excess requests wait briefly for a pooled connection rather than failing. The PostgreSQL connections alert (80% of the limit) watches this constraint, and PgBouncer on General Purpose is the production step that removes it.

## Network

- Virtual network: `10.60.0.0/16` (no cost by itself).
- Container Apps environment subnet: `10.60.0.0/23`, delegated to `Microsoft.App/environments`, with service endpoints `Microsoft.Storage`, `Microsoft.KeyVault`, and `Microsoft.EventHub`.
- PostgreSQL subnet: `10.60.2.0/24`, delegated to `Microsoft.DBforPostgreSQL/flexibleServers`, with private DNS zone `shortener.private.postgres.database.azure.com` linked to the VNet. The server has no public endpoint; only resources in the VNet can reach it. Migrations run as a job inside the environment, so no laptop access is needed.
- No private endpoints for Key Vault, Storage, or Event Hubs. Service endpoints plus firewall rules restrict them to the Container Apps subnet at no cost; private endpoints (about USD 7–8/month each) are the documented production step.
- Storage, Key Vault, and Event Hubs network rules allow only the Container Apps subnet (via service endpoints), plus the deploying operator's current public IP while Track C provisions; Track C removes the operator IP afterwards and records it.
- PostgreSQL requires TLS and a strong password in addition to private network access.
- All data access uses TLS. Key Vault, Storage, and registry access use managed identities and RBAC.

## Image build (Track C)

The root `Dockerfile` has stages `api`, `web`, and `analytics`, selected with `--target` and build arg `PROJECT`. The `azure.yaml` schema has no Docker target setting, and the `Dockerfile` stays unchanged. So:

- `infra/scripts/build-images.sh` runs `az acr build --registry <acr> --target <stage> --build-arg PROJECT=<csproj> --file Dockerfile .` once per service from the repo root. Projects: `src/Shortener.Api/Shortener.Api.csproj`, `src/Shortener.Web/Shortener.Web.csproj`, `src/Shortener.Analytics/Shortener.Analytics.csproj`.
- It reads each pushed image's digest and runs `azd env set SERVICE_API_IMAGE <acr>/shortener-api@sha256:...` (likewise `SERVICE_WEB_IMAGE`, `SERVICE_ANALYTICS_IMAGE`).
- `azure.yaml` declares each service with `host: containerapp` and `image: ${SERVICE_<NAME>_IMAGE}` and runs the script as a `predeploy` hook.
- The migration job uses `SERVICE_API_IMAGE`. Redis and Caddy use pinned public image digests.

## Public routing (mirrors `deploy/Caddyfile.*`)

- `/api/*`, `/r/*`, `/health/*` → API. Everything else → Web.
- Preferred: Container Apps environment path-based HTTP routing, if it is generally available in `centralus` and supports WebSockets and Web's sticky sessions. Track C verifies this in Microsoft documentation and records the URL.
- Otherwise: `ca-shortener-proxy` running Caddy with external ingress, configured like `deploy/Caddyfile.local`, forwarding to the internal API and Web apps with `header_up Host {upstream_hostport}` so Container Apps can route, and trusting private ranges so `X-Forwarded-Proto` and `X-Forwarded-Host` from Container Apps ingress pass through.
- API and Web ingress are internal-only when a proxy is used; external when native routing is used.
- Public host: the routing endpoint's Container Apps hostname, or `CUSTOM_DOMAIN` when set (Container Apps managed certificate).
- Web calls API privately at `http://ca-shortener-api/`.

## Configuration names

All settings use .NET double-underscore environment variable form. Every mode switch fails at startup with a message naming the missing setting. No mode silently falls back to another. Defaults for local development live in `appsettings.json` (Track A), and Azure overrides them with environment variables.

### Shared

| Setting | Local Compose | Azure |
|---|---|---|
| `ConnectionStrings__Redis` | `redis:6379` | `ca-shortener-redis:6379,password=<pw>,abortConnect=False` (Key Vault secret `redis-connection`) |
| `ConnectionStrings__ShortenerDb` (apps) | `Host=db;Port=5432;...` | `Host=<pg>.postgres.database.azure.com;Port=5432;Database=shortener;Username=shortener;Password=<pw>;SSL Mode=Require;Maximum Pool Size=<per connection budget>` (Key Vault secret `pg-app-connection`) |
| `ConnectionStrings__ShortenerDb` (migrate job) | `Host=db;Port=5432;...` | Same, without `Maximum Pool Size` (Key Vault secret `pg-migrate-connection`) |
| `AzureAd__ClientId` (API and migrate job) | private env file | `API_CLIENT_ID` from the `azd` environment: `34d681eb-4c10-402d-b1dd-1b02ad2d8d89` |
| `AZURE_CLIENT_ID` | unset | client ID of the app's user-assigned managed identity |

### Identity (Entra)

Azure runs `Identity__Mode=Workforce` by default, using the existing registrations in tenant `mediaraq com`. Customer (External ID) mode is a separate, later choice, because the Web app runs one identity mode per deployment.

| Setting | App | Azure value |
|---|---|---|
| `Identity__Mode` | Web | `Workforce` |
| `AzureAd__Instance` | Web | `https://login.microsoftonline.com/` |
| `AzureAd__TenantId` | API, Web, migrate | `organizations` |
| `AzureAd__ClientId` | API, migrate | `API_CLIENT_ID` = `34d681eb-4c10-402d-b1dd-1b02ad2d8d89` (SchwabUrlShortener API) |
| `AzureAd__ClientId` | Web | `WEB_CLIENT_ID` = `d4cd63a0-5583-41ad-a640-803ea76bb3a8` (SchwabUrlShortener Web) |
| `AzureAd__ClientSecret` | Web | Key Vault secret `web-client-secret`, written by Bicep from the secure `azd` value `WEB_CLIENT_SECRET` |
| `ShortenerApi__Scope` | Web | `api://34d681eb-4c10-402d-b1dd-1b02ad2d8d89/access_as_user` |

### Secret handling

- Every runtime secret lives in Key Vault and reaches apps only as a Container Apps Key Vault reference resolved with that app's own managed identity: `pg-app-connection`, `pg-migrate-connection`, `eventhubs-connection`, `redis-connection`, `redis-password`, `web-client-secret`.
- Deployment-time inputs (`POSTGRES_PASSWORD`, `WEB_CLIENT_SECRET`) live only in the Git-ignored local `azd` environment file and are passed to Bicep as `@secure()` parameters. Bicep writes them into Key Vault through the control plane, so Key Vault's network rules do not block it. They never appear in outputs, logs, or chat.
- One platform exception: Container Apps mounts Azure Files only with the storage account key, held in the environment's encrypted storage definition. It is scoped to the separate Redis persistence account; the Data Protection account keeps shared-key access disabled.
- Passwordless access (Entra authentication to PostgreSQL and Event Hubs) removes the database password and Event Hubs connection string entirely. It needs application code changes and is a documented next step.

### Managed identities (one per service, least privilege)

Every service that touches an Azure resource has its own user-assigned managed identity. No identity is shared. Key Vault access is granted per secret or per key (role assignments scoped to the individual secret or key), never vault-wide. `AZURE_CLIENT_ID` on each app is its own identity's client ID.

| Identity | Used by | Roles, and nothing else |
|---|---|---|
| `id-shortener-api` | `ca-shortener-api` | `AcrPull` on the registry; `Key Vault Secrets User` on `pg-app-connection`, `redis-connection`, `eventhubs-connection`; `Key Vault Crypto User` on key `dataprotection-api`; `Storage Blob Data Contributor` on blob container `dataprotection` |
| `id-shortener-web` | `ca-shortener-web` | `AcrPull`; `Key Vault Secrets User` on `redis-connection`, `web-client-secret`; `Key Vault Crypto User` on key `dataprotection-web`; `Storage Blob Data Contributor` on blob container `dataprotection` |
| `id-shortener-analytics` | `ca-shortener-analytics` | `AcrPull`; `Key Vault Secrets User` on `pg-app-connection`, `eventhubs-connection`, `redis-connection` |
| `id-shortener-migrate` | `caj-shortener-migrate` | `AcrPull`; `Key Vault Secrets User` on `pg-migrate-connection` |
| `id-shortener-redis` | `ca-shortener-redis` | `Key Vault Secrets User` on `redis-password` (the bare password the Redis server starts with; `redis-connection` holds the client connection string) |
| `id-shortener-proxy` | `ca-shortener-proxy`, only if deployed | none beyond pulling its public image; no Key Vault access |

The KEDA Kafka scale rule on Analytics reads `eventhubs-connection` through the Analytics identity.

Where identities stop: PostgreSQL and Event Hubs authenticate with a password and connection string that each app fetches from Key Vault with its identity. Open-source Redis has no Entra support, so its password comes from Key Vault the same way. Entra authentication to PostgreSQL and Event Hubs (removing those secrets) is the documented code-change step.

### Data Protection (Track A)

| Setting | Local Compose | Azure |
|---|---|---|
| `DataProtection__Mode` | `Filesystem` | `AzureBlob` |
| `DataProtection__ApplicationName` | `Shortener.Api` or `Shortener.Web` | same |
| `DataProtection__BlobUri` | unset | `https://<storage>.blob.core.windows.net/dataprotection/api-keys.xml` or `.../web-keys.xml` |
| `DataProtection__KeyVaultKeyId` | unset | `https://<vault>.vault.azure.net/keys/dataprotection-api` or `.../dataprotection-web` |

### Token cache (Track A, Web only)

| Setting | Local Compose | Azure |
|---|---|---|
| `TokenCache__Mode` | `Distributed` | `Distributed` |
| `TokenCache__InstanceName` | `shortener:web:tokens:` | same |

### Forwarded headers (Track A)

| Setting | Local Compose | Azure |
|---|---|---|
| `ForwardedHeaders__KnownProxyHost` | `proxy` | unset |
| `ForwardedHeaders__KnownNetworks` | unset | `10.60.0.0/23` |
| `ForwardedHeaders__ForwardLimit` | `1` | the number of proxies in front of the app: `1` with native routing (Container Apps ingress), `3` with the Caddy proxy (ingress, Caddy, ingress). Track C verifies by checking that the app sees the public host and `https`. |

At most one of `KnownProxyHost` or `KnownNetworks` may be set; both set fails at startup.

### Allowed hosts

Container Apps ingress routes by host name, so each app receives its own Container Apps FQDN as `Host`, with the public host in `X-Forwarded-Host`. Host filtering runs before forwarded headers are applied, so each app must allow its own FQDN.

- API: `AllowedHosts` = `ca-shortener-api.<environment default domain>;ca-shortener-api;<public host>`.
- Web: `AllowedHosts` = `ca-shortener-web.<environment default domain>;<public host>`.
- Bicep builds these from the environment's `defaultDomain` output, so there is no dependency cycle.
- `ShortenerApi__BaseUrl` = `http://ca-shortener-api/` and `ShortenerApi__AllowedInternalHttpHosts` = `ca-shortener-api`. If in-environment HTTP is redirected to HTTPS, set `allowInsecure: true` on the API ingress only.
- `PublicLinks__BaseUrl` = `https://<public host>/` on API and Web.

### Kafka / Event Hubs (Track A)

| Setting | Local Compose | Azure |
|---|---|---|
| `Kafka__BootstrapServers` | `kafka:9092` | `<namespace>.servicebus.windows.net:9093` |
| `Kafka__Topic` | `managed-clicks-v2` | `managed-clicks-v2` |
| `Kafka__ConsumerGroup` | `shortener-analytics-v1` | `shortener-analytics-v1` |
| `Kafka__SecurityProtocol` | `Plaintext` (default) | `SaslSsl` |
| `Kafka__SaslMechanism` | unset | `Plain` |
| `Kafka__SaslUsername` | unset | `$ConnectionString` |
| `Kafka__SaslPassword` | unset | Event Hubs namespace connection string (Key Vault secret `eventhubs-connection`) |
| `Kafka__EnableIdempotence` | `true` | `true` (Track A confirmed Event Hubs support from Microsoft documentation) |

### Live updates (Track B)

| Setting | Local Compose | Azure |
|---|---|---|
| `LiveUpdates__Mode` | `Redis` | `Redis` |
| `LiveUpdates__Channel` | `shortener:links:v1` | `shortener:links:v1` |
| `LiveUpdates__DebounceMilliseconds` | `1000` | `1000` |

One channel. Payload is a hint only:

```json
{ "v": 1, "scope": "tenant" | "workspace", "scopeId": "<guid>", "shortCode": "<code>" }
```

No destination URLs, counts, user identifiers, or tokens are ever published.

### Analytics health (Track A)

The Analytics worker serves `GET /health/live` and `GET /health/ready` on port `8080`. The `--healthcheck` command stays for Compose.

## Compose ownership

- Track A owns the `web` service lines `ConnectionStrings__Redis`, `TokenCache__Mode`, `DataProtection__*`, and `depends_on: redis`, the `api` service Data Protection lines, and the `analytics` health port.
- Track B owns the `analytics` service line `ConnectionStrings__Redis` and the `LiveUpdates__*` lines on `web`, `api`, and `analytics`.
- Track C does not edit `compose.yaml`.

## Source ownership

| Area | Owner |
|---|---|
| `src/Shortener.Web/Program.cs`, `src/Shortener.Api/Program.cs` startup configuration, `appsettings.json` defaults | A (B adds `AddLiveUpdates(...)` registration, rebased after A) |
| `src/Shortener.Analytics/AnalyticsOptions.cs`, `Program.cs`, health endpoints, `Dockerfile` analytics stage | A |
| `compose.scaleout.yaml`, `deploy/Caddyfile.scaleout` | A |
| `src/Shortener.Web/Components/Pages/Dashboard.razor`, `src/Shortener.Web/Services/LiveUpdates/**` | B |
| `ILinkChangePublisher` in `src/Shortener.Application`, Redis publisher in `src/Shortener.Infrastructure`, publish calls in Analytics and API | B |
| `infra/**`, `azure.yaml` | C |

## Merge order and deployments

1. A merges to `main` first (fast-forward).
2. B rebases on `main`, then merges.
3. C can merge at any time; it never touches `src/`.
4. First deploy: data services and apps with current `main` images; guest flow smoke test.
5. After A and B merge: redeploy with all scale-out and live-update settings enabled, then the scale demonstration.

## Human prerequisites (Michael)

- `az login` and `azd auth login` as `social@mediaraq.com` (done 2026-10-13).
- Owner on the subscription (confirmed 2026-10-13).
- `POSTGRES_PASSWORD` in the `azd` environment; Track C copies it from the deleted staging environment's local file without displaying it.
- Set `WEB_CLIENT_SECRET` in the `azd` environment from the private file used locally (never in chat). If that secret has expired, create a new one on the SchwabUrlShortener Web registration under **Certificates & secrets**.
- Entra redirect URIs for the public host on the **SchwabUrlShortener Web** registration (`d4cd63a0-5583-41ad-a640-803ea76bb3a8`).
- Upgrade the free trial to Pay-As-You-Go before it ends.
- After sign-in works: sign in once and create one managed demo link for the scale demonstration (Track C names the short code it needs).
- Optional: a custom domain and DNS records.
