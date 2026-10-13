# Azure scale-out contract — 2026-10-13 17:20 CDT

Status: agreed baseline for Tracks A, B, and C. Revised 2026-10-13 17:50 CDT: Kafka topic corrected to `managed-clicks-v2`, internal API setting corrected to `ShortenerApi__AllowedInternalHttpHosts`, image build method added. Revised 17:58 CDT: Azure account, subscription, and tenant recorded. Revised 18:10 CDT: origin FQDNs added to `AllowedHosts` for Front Door. Change it only by editing this file and telling every active track.

## Purpose

Three tracks run in parallel from `origin/main` at `66b8318a351788d6cec5ba6e3bfa3f0d0ed63ba0`:

| Track | Handoff | Branch | Worktree |
|---|---|---|---|
| A — scale-out readiness | `2026-10-13-1720-track-a-scaleout-readiness-handoff.md` | `codex/azure-scaleout-readiness` | `/Users/michaelinman/Repos/SchwabUrlShortener-scaleout` |
| B — dashboard live push | `2026-10-13-1720-track-b-dashboard-live-push-handoff.md` | `codex/dashboard-live-push` | `/Users/michaelinman/Repos/SchwabUrlShortener-livepush` |
| C — Azure infrastructure | `2026-10-13-1720-track-c-azure-infrastructure-handoff.md` | `codex/azure-infra` | `/Users/michaelinman/Repos/SchwabUrlShortener-infra` |

This contract fixes the names and values the tracks share, so no track waits on another. The application is modified, not rewritten. Local Compose remains a supported way to run the whole system.

## Environment

- Azure account: `social@mediaraq.com`. All Azure work for this project uses this login.
- Subscription: `Azure subscription 1`, ID `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, in tenant `mediaraq com` (`eea269ab-1571-48a5-a18c-824ba5859cd5`). Confirmed by Michael on 2026-10-13 for staging. This is the subscription that already holds `rg-schwab-url-shortener-identity`. Before any deployment, `az account show` must report this subscription ID; if it does not, run `az account set --subscription baaf4acd-ff40-43d1-a7ba-6089a2c4f41e` and recheck. The same login can also see two subscriptions in tenant `3d9ba6ab-7f64-43fa-bf44-346b3c2a3fb6`; never deploy there.
- Do not modify or reuse resources in `rg-schwab-url-shortener-identity` or any other existing resource group in this subscription (`rg-agentengineering-*`, `AzureLeadInterviewLab-rg`, `rg-mynetapp-dev`, `NetworkWatcherRG`).
- Azure region: `centralus`. If any required SKU (Azure Managed Redis, PostgreSQL Flexible Server General Purpose with built-in PgBouncer, Container Apps workload profiles, Event Hubs Standard) is unavailable there, use `eastus2` and record the reason.
- Resource group: `rg-shortener-staging`.
- Naming: `<type>-shortener-<service>-stg`, for example `ca-shortener-api-stg`. Globally unique names (storage, Key Vault, registry, Redis, Event Hubs, PostgreSQL) append `uniqueString(resourceGroup().id)` truncated to 6 characters.
- This is a Schwab-only resource boundary. Nothing references, reuses, or peers with any AI-Influencer-Factory resource, subscription resource group, registry, network, or identity.

## Network

- Virtual network: `10.60.0.0/16`.
- Container Apps environment subnet: `10.60.0.0/23`, delegated to `Microsoft.App/environments`.
- Private endpoint subnet: `10.60.4.0/24`.
- Reserved, unused: `10.60.5.0/24` (future PostgreSQL delegated subnet if private access is switched from private endpoint to VNet injection).

## Image build (Track C)

The root `Dockerfile` has stages `api`, `web`, and `analytics`, selected with `--target` and build arg `PROJECT`. The `azure.yaml` schema has no Docker target setting, and the `Dockerfile` stays unchanged. So:

- `infra/scripts/build-images.sh` runs `az acr build --registry <acr> --target <stage> --build-arg PROJECT=<csproj> --file Dockerfile .` once per service, from the repo root. Projects: `src/Shortener.Api/Shortener.Api.csproj`, `src/Shortener.Web/Shortener.Web.csproj`, `src/Shortener.Analytics/Shortener.Analytics.csproj`.
- It reads each pushed image's digest and runs `azd env set SERVICE_API_IMAGE <acr>/shortener-api@sha256:...` (likewise `SERVICE_WEB_IMAGE`, `SERVICE_ANALYTICS_IMAGE`).
- `azure.yaml` declares each service with `host: containerapp` and `image: ${SERVICE_<NAME>_IMAGE}` (prebuilt-image form) and runs the script as a `predeploy` hook.
- The migration job uses `SERVICE_API_IMAGE`.

Remote ACR builds mean no local Docker is needed, and images are always deployed by digest.

## Public routing (mirrors `deploy/Caddyfile.*`)

- `/api/*`, `/r/*`, `/health/*` → API.
- Everything else → Web.
- Web calls API privately at `http://ca-shortener-api-stg/` inside the Container Apps environment.

## Configuration names

All settings use .NET double-underscore environment variable form. Every mode switch fails at startup with a message naming the missing setting. No mode silently falls back to another.

### Shared

| Setting | Local Compose | Azure staging |
|---|---|---|
| `ConnectionStrings__Redis` | `redis:6379` | Azure Managed Redis: `<host>:10000,password=<key>,ssl=True,abortConnect=False` (Key Vault secret `redis-connection`) |
| `ConnectionStrings__ShortenerDb` (apps) | `Host=db;Port=5432;...` | `Host=<pg>.postgres.database.azure.com;Port=6432;Database=shortener;Username=<user>;Password=<pw>;SSL Mode=Require;No Reset On Close=true` (Key Vault secret `pg-app-connection`, via PgBouncer in transaction mode) |
| `ConnectionStrings__ShortenerDb` (migrate job only) | `Host=db;Port=5432;...` | Same host, `Port=5432` direct, bypassing PgBouncer (Key Vault secret `pg-migrate-connection`) |
| `AZURE_CLIENT_ID` | unset | client ID of the app's user-assigned managed identity |

### Data Protection (Track A)

| Setting | Local Compose | Azure staging |
|---|---|---|
| `DataProtection__Mode` | `Filesystem` | `AzureBlob` |
| `DataProtection__ApplicationName` | `Shortener.Api` or `Shortener.Web` | same |
| `DataProtection__BlobUri` | unset | `https://<storage>.blob.core.windows.net/dataprotection/api-keys.xml` or `.../web-keys.xml` |
| `DataProtection__KeyVaultKeyId` | unset | `https://<vault>.vault.azure.net/keys/dataprotection-api` or `.../dataprotection-web` |

`Filesystem` keeps using `/root/.aspnet/DataProtection-Keys` on the existing named volumes.

### Token cache (Track A, Web only)

| Setting | Local Compose | Azure staging |
|---|---|---|
| `TokenCache__Mode` | `Distributed` | `Distributed` |
| `TokenCache__InstanceName` | `shortener:web:tokens:` | same |

`InMemory` remains a valid explicit value for single-instance development only. `Distributed` requires `ConnectionStrings__Redis`.

### Forwarded headers (Track A)

| Setting | Local Compose | Azure staging |
|---|---|---|
| `ForwardedHeaders__KnownProxyHost` | `proxy` | unset |
| `ForwardedHeaders__KnownNetworks` | unset | `10.60.0.0/23` |
| `ForwardedHeaders__ForwardLimit` | `1` (default) | `2` (Front Door, then Container Apps ingress) |

At most one of `KnownProxyHost` or `KnownNetworks` may be set; both set fails at startup. Neither set means no forwarded headers are trusted, as today.

### Allowed hosts

Front Door sends each request to Container Apps with `Host` set to that app's origin FQDN (`<app>.<environment default domain>`), because Container Apps ingress routes by host name and would return 404 for the Front Door or custom host. The public host reaches the app in `X-Forwarded-Host`. ASP.NET Core host filtering runs before forwarded headers are applied, so each app must allow its own origin FQDN.

- API, Azure: `AllowedHosts` = `ca-shortener-api-stg.<environment default domain>;ca-shortener-api-stg;<public host>`.
- Web, Azure: `AllowedHosts` = `ca-shortener-web-stg.<environment default domain>;<public host>`.
- Bicep builds the origin FQDNs from the Container Apps environment's `defaultDomain` output, which exists before the apps are created, so there is no dependency cycle. `<public host>` is the Front Door endpoint host, or `CUSTOM_DOMAIN` when set.
- Front Door origins keep the origin host header equal to the origin FQDN.
- `ShortenerApi__BaseUrl` = `http://ca-shortener-api-stg/` and `ShortenerApi__AllowedInternalHttpHosts` = `ca-shortener-api-stg` (the setting today's Web image reads; Compose uses `api`). If Container Apps redirects in-environment HTTP to HTTPS, set `allowInsecure: true` on the API ingress only; Front Door stays HTTPS-only.
- `PublicLinks__BaseUrl` = `https://<public host>/` on API and Web, so generated short links always use the public host, whatever `Host` the request arrived with.
- Until Track A's `ForwardedHeaders__KnownNetworks` is enabled, apps see the origin FQDN as the request host. Guest flows work; Entra sign-in does not, because the redirect URI would be built from the origin FQDN. This is expected for the first deployment.

### Kafka / Event Hubs (Track A)

| Setting | Local Compose | Azure staging |
|---|---|---|
| `Kafka__BootstrapServers` | `kafka:9092` | `<namespace>.servicebus.windows.net:9093` |
| `Kafka__Topic` | `managed-clicks-v2` | `managed-clicks-v2` |
| `Kafka__ConsumerGroup` | `shortener-analytics-v1` | `shortener-analytics-v1` |
| `Kafka__SecurityProtocol` | `Plaintext` (default) | `SaslSsl` |
| `Kafka__SaslMechanism` | unset | `Plain` |
| `Kafka__SaslUsername` | unset | `$ConnectionString` |
| `Kafka__SaslPassword` | unset | Event Hubs namespace connection string (Key Vault secret `eventhubs-connection`) |
| `Kafka__EnableIdempotence` | `true` (default) | the value Track A confirms Event Hubs supports; consumer event-ID dedup already prevents double counting either way |

Event hub `managed-clicks-v2`: 32 partitions (fixed at creation on Standard), 7-day retention. Consumer group `shortener-analytics-v1`.

### Live updates (Track B)

| Setting | Local Compose | Azure staging |
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

Container Apps probes are HTTP or TCP only, so the Analytics worker serves `GET /health/live` and `GET /health/ready` on port `8080`, with the same dependency checks as today's `--healthcheck` command. The `--healthcheck` command stays for Compose.

## Compose ownership

To keep merges trivial, each track edits only its own hunks of `compose.yaml`:

- Track A owns the `web` service lines: add `ConnectionStrings__Redis: redis:6379`, `TokenCache__Mode: Distributed`, `DataProtection__Mode: Filesystem`, `DataProtection__ApplicationName: Shortener.Web`, and `depends_on: redis: condition: service_healthy`. Track A also owns the `api` service Data Protection lines and the `analytics` health port.
- Track B owns the `analytics` service line `ConnectionStrings__Redis: redis:6379` and the `LiveUpdates__*` lines on `web`, `api`, and `analytics`. If Track B needs Web connected to Redis before Track A merges, it adds exactly Track A's `ConnectionStrings__Redis` and `depends_on` lines above, so the merge is identical text.
- Track C does not edit `compose.yaml`.

## Source ownership

| Area | Owner |
|---|---|
| `src/Shortener.Web/Program.cs`, `src/Shortener.Api/Program.cs` startup configuration | A (B adds one `AddLiveUpdates(...)` line per host, rebased after A) |
| `src/Shortener.Analytics/AnalyticsOptions.cs`, `Program.cs`, health endpoints, `Dockerfile` analytics stage | A |
| `compose.scaleout.yaml`, `deploy/Caddyfile.scaleout` | A |
| `src/Shortener.Web/Components/Pages/Dashboard.razor`, `src/Shortener.Web/Services/LiveUpdates/**` | B |
| `ILinkChangePublisher` in `src/Shortener.Application`, Redis publisher in `src/Shortener.Infrastructure`, publish calls in Analytics consumer and API create endpoints | B |
| `infra/**`, `azure.yaml` | C |

## Merge order and deployments

1. A merges to `main` first.
2. B rebases on `main`, then merges.
3. C can merge at any time; it never touches `src/`.
4. First Azure deploy (end of Track C phase C3): current `main` images, one replica each.
5. Second deploy: after A merges, API and Web scale to at least two replicas, Web with sticky sessions.
6. Third deploy: after B merges, then staging validation (Track E, separate handoff).

## Human prerequisites (Michael)

- Azure subscription confirmed above. Michael needs Owner, or Contributor plus User Access Administrator, on `rg-shortener-staging` as `social@mediaraq.com`.
- `az login` and `azd auth login` as `social@mediaraq.com` on this machine for Track C deployments.
- Staging domain and DNS access. Until provided, Track C uses the Front Door `*.azurefd.net` host.
- Entra redirect URIs for the staging host on the workforce and External ID Web app registrations.
- Budget alert on the resource group.
- Event Hubs authentication: connection string for staging (above). Managed identity with OAuth is a later, separate change.
