# Azure infrastructure — 2026-10-13

This directory implements the [lean Azure contract](../docs/2026-10-13-1720-azure-scaleout-contract.md) for the single `prod` environment in `rg-schwab-url-shortener`, `centralus`. Read the [validation record](../docs/2026-10-13-1757-track-c-azure-infrastructure-validation.md) before provisioning. The retired staging resource group must never be recreated. The local `.azure/staging` folder is preserved at Michael's request.

## Design principles

Expected load is low, so warm API/Web/Analytics replicas and one Event Hubs throughput unit serve ordinary traffic. Container Apps autoscale must expand and retract replicas under measured load; Event Hubs auto-inflate is disabled because it only raises capacity. Private PostgreSQL, subnet service endpoints, managed identities, RBAC, TLS, and Key Vault retain security on the small budget. Pool size 2 and ingress alerts protect the B1ms and Event Hubs limits. A bounded anonymous-redirect demo must show scale-out and scale-in. Each lean resource and its upgrade are listed below.

## P1: foundation and data

`main.bicep` and `main.parameters.json` are the azd entry point. They currently accept only `DEPLOYMENT_PHASE=p1`. `p1-preview.bicep` is a subscription-scope **what-if only** wrapper so a new resource group and all of P1 can be previewed together; never deploy the wrapper. `resource-group.bicep` defines only the resource group for a later, approved deployment. `main.prod.bicepparam` supports offline compilation and local review without placing values in source control.

The `prod` azd environment holds `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, `AZURE_LOCATION`, `DEPLOYMENT_PHASE`, `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `WEB_CLIENT_SECRET`, `BUDGET_CONTACT_EMAIL`, `API_CLIENT_ID`, `WEB_CLIENT_ID`, and the current `OPERATOR_PUBLIC_IP`. The password and client-secret values are local, private azd values; never print, log, or commit them. P1 uses those secure values and the budget contact to create Key Vault secrets. P2 will use `API_CLIENT_ID` for `AzureAd__ClientId` on API and the migration job, and `WEB_CLIENT_ID` plus the Key Vault Web secret for Web. Check account and subscription before any Azure write. Michael approves each phase after a new what-if. Provider registration and resource creation are part of the P1 approval, not this offline slice.

P1 creates the private PostgreSQL server, VNet and DNS, Event Hubs Standard at fixed 1 TU, Key Vault, ACR Basic, Data Protection storage, separate Redis Azure Files storage, five service identities, logging, budget, and feasible PostgreSQL and Event Hubs ingress alerts. The app PostgreSQL secret includes `Maximum Pool Size=2`. At maximum planned scale the connection budget is API `5×2=10`, Analytics `4×(2+1)=12` (the readiness check has a separate pool), migration `1`, total **23 of 35 user connections**. The PostgreSQL alert fires above 28 active connections (80% of 35) for 10 minutes. Separate Event Hubs alerts fire when average ingress over 10 minutes exceeds 0.7 MB/s or 700 events/s. Verify the server's actual `max_connections` and reserved slots after provisioning.

The 19:35 baseline adds the contract's five tags to every P1 resource type that accepts tags (including the new group), explicit PostgreSQL TLS 1.2 and required transport, Storage TLS 1.2 and HTTPS-only access with public blobs disabled, Event Hubs TLS 1.2, and ACR with anonymous pull and admin access disabled. Key Vault TLS 1.2 is a client/runtime setting to verify in P2; the vault resource has no minimum-TLS property. Child types such as subnets, diagnostic settings, budgets, role assignments, database/configuration objects, shares, and locks do not expose tags in these Bicep schemas; their taggable parents carry the tags. P1 also defines a shared [Azure Monitor workbook](workbooks/shortener-overview.json). Several panels need Track D telemetry and P2 Container Apps diagnostics before they show data.

The subscription-scope [`CloudPosture` Free pricing resource](subscription-foundational-cspm.bicep) requests only foundational CSPM. It needs `Microsoft.Security` registration as part of an approved P1. Record the secure score and resource-group recommendations after provisioning; neither can be verified from the template. Apply the [`CanNotDelete` group lock](p1-lock.bicep) **last**, after all foundation resources, free posture setting, and their checks. The combined `p1-preview.bicep` models this dependency for what-if only. Approved deployment order is: recheck account/group and preview; register required providers; create the new group via `resource-group.bicep`; provision `main.bicep`; deploy `subscription-foundational-cspm.bicep`; verify; deploy `p1-lock.bicep`. Do not deploy the preview wrapper.

The 2026-10-13 P1 attempt partially deployed because Key Vault blocked ARM key creation with `bypass: None`. The 20:10 contract permits `AzureServices` while keeping `defaultAction: Deny` and the subnet rule. [`p1-vault-network.bicep`](p1-vault-network.bicep) contains only that vault resource for a focused bypass preview. [`p1-resume.bicep`](p1-resume.bicep) contains only the two missing keys, their scoped RBAC grants, and the monitoring module, so retrying does not reapply secrets or defaulted properties on already-created resources. Preview and deploy those templates separately, then preview and deploy the lock last. The [validation record](../docs/2026-10-13-1757-track-c-azure-infrastructure-validation.md) records the exact changes and live results.

| Identity | P1 role scopes |
| --- | --- |
| API | ACR pull; `pg-app-connection`, `redis-connection`; its Data Protection key; Data Protection blob container |
| Web | ACR pull; `redis-connection`, `web-client-secret`; its Data Protection key; Data Protection blob container |
| Analytics | ACR pull; `pg-app-connection`, `redis-connection`, `eventhubs-connection` |
| Migrate | ACR pull; only `pg-migrate-connection` |
| Redis | Only `redis-password` |

Each secret and key grant is scoped to that individual resource. There is no vault-wide grant. A proxy identity is created in P2 only if proxy routing is chosen and has no Key Vault access.

The Event Hubs Standard tier has no platform `ConsumerLag` metric. Once Track D exports the Analytics app's own `shortener.analytics.consumer_lag` metric, add an alert on that metric for lag above 1,000 for 10 minutes; do not create an Event Hubs platform lag alert. The app 5xx, restart, and outbox alerts likewise require app/log telemetry in P2/P3. Do not count them as active P1 coverage. See the validation record for the phase boundary.

## Later phases

P2 is authored in [`p2-apps.bicep`](p2-apps.bicep) and its [dated deployment plan](../docs/2026-10-13-2135-track-c-p2-apps-deployment-plan.md). Michael's later instruction moves Track A/B scale-out settings into P2: `enableScaleOutSettings=true`, Analytics min 1/max 4, and Kafka idempotence true; Track D telemetry remains P3. P2 was deployed from real image digests after a provider-backed preview; the [runtime validation](../docs/2026-10-13-2254-track-c-p2-runtime-validation.md) records migration, smoke tests, runtime repairs, and operator IP cleanup. The azd `main.bicep` entry point still deploys P1; P2 was deployed with its separate template.

- P2: the Container Apps environment, routing, Redis, API, Web, Analytics, and migration job were deployed after a provider-backed what-if with real image digests. The 23:20 identity correction runs API, Web, and migration in `Customer` mode with the Ziplio External ID tenant and customer app registrations. [`scripts/deploy-customer-identity.py`](scripts/deploy-customer-identity.py) isolates these three resources and the secure Key Vault `web-client-secret` write for provider preview and deployment. The Web managed identity belongs to a different tenant from the customer registration, so this deployment keeps its Key Vault client secret. Redis uses its own managed identity for the raw password and an Azure Files mount.
- P3: add Track D telemetry and run the approved anonymous redirect scale demonstration against one managed demo link. It must measure replica and consumer-lag movement without directly editing an application database.

P2 routing must be decided from current Container Apps documentation and verified for WebSocket and sticky-session support. The old Front Door and staging templates were removed from the active infrastructure tree; their history remains in Git.

## Production upgrade path

| Lean choice | Upgrade |
| --- | --- |
| PostgreSQL B1ms, single zone, 7-day backups | General Purpose with HA and PgBouncer after measuring connections and cost |
| Event Hubs Standard, fixed one throughput unit | Raise throughput units deliberately after the ingress alert fires; add app-emitted lag telemetry |
| Redis container with Azure Files | Managed Redis when regional capacity and budget permit |
| Service endpoints for Key Vault, Storage, Event Hubs | Private endpoints and tighter egress controls |
| ACR Basic | Upgrade tier for geo-replication or higher throughput |
| One region and Log Analytics 1 GB/day cap | Multi-region recovery and a higher log cap with cost alerts |
| Azure budget alerts | Add billing review and operational paging; budget alerts do not cap spend |

## Operations

- [Recovery runbook](../docs/runbooks/2026-10-13-1935-recovery.md): private PostgreSQL point-in-time restore, controlled cutover, migration check, and Redis cache recovery.
- [Image rollback runbook](../docs/runbooks/2026-10-13-1935-image-rollback.md): retain and redeploy the previous digest for single-revision apps.
- [Secret rotation runbook](../docs/runbooks/2026-10-13-1935-secret-rotation.md): stage new Key Vault versions and validate before retiring old credentials.
- The free trial credit was reported on 2026-10-13 as expiring in about 17 days, around **2026-10-30**. Confirm the billing date and upgrade before expiry if evaluation continues; the $100 budget only sends alerts.

## Entra registration reconciliation — updated 2026-10-13 23:22 CDT

[`entra/web-registration.json`](entra/web-registration.json) declares the customer Web app client ID, local redirects, and callback paths. [`scripts/configure-entra.sh`](scripts/configure-entra.sh) reads `PUBLIC_HOST`, optional `CUSTOM_DOMAIN`, and optional `PREVIOUS_CUSTOM_DOMAIN` from the `prod` azd environment and reads the registration with Microsoft Graph through `AZURE_CONFIG_DIR=$HOME/.azure-ziplio` (or `ENTRA_AZURE_CONFIG_DIR`). It verifies the Ziplio tenant before Graph calls and never switches the default Azure-resource profile. Run `./infra/scripts/configure-entra.sh --dry-run` before any update. The `postprovision` hook runs `--apply`; it prints additions/removals, preserves unrecognized URIs with a warning, and patches only `web.redirectUris` when the set changes. In CI the hook skips Entra unless `ENTRA_RECONCILE_CI_ENABLED=true`; Track F must add the deployment identity as registration owner and grant `Application.ReadWrite.OwnedBy` first. No secret or token is printed by the script.

The script makes no federated-credential changes. Microsoft requires the managed identity and app registration to be in the same tenant for this trust; the Web identity and customer registration are in different tenants. The running Web app uses the Key Vault `web-client-secret`. The [Entra runbook](../docs/runbooks/2026-10-13-2307-entra-registration-and-domain.md) predates the customer correction; use the contract's 23:20 Identity section and the [customer cutover validation](../docs/2026-10-13-2322-track-c-customer-identity-cutover-validation.md) for current configuration.

`CUSTOM_DOMAIN` currently adds the two custom-domain callbacks in the script's desired set, but it **does not yet bind a route, obtain a managed certificate, or update application `AllowedHosts`/`PublicLinks__BaseUrl`**. The current `azd provision` entry point only deploys P1. Do not set a real custom domain or switch `PUBLIC_HOST` until the P2 domain Bicep, DNS verification, certificate binding, host settings, and HTTPS smoke are implemented and previewed. Azure's route Auto binding starts as HTTP until the managed certificate is created.
