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

| Identity | P1 role scopes |
| --- | --- |
| API | ACR pull; `pg-app-connection`, `redis-connection`, `eventhubs-connection`; its Data Protection key; Data Protection blob container |
| Web | ACR pull; `redis-connection`, `web-client-secret`; its Data Protection key; Data Protection blob container |
| Analytics | ACR pull; `pg-app-connection`, `redis-connection`, `eventhubs-connection` |
| Migrate | ACR pull; only `pg-migrate-connection` |
| Redis | Only `redis-password` |

Each secret and key grant is scoped to that individual resource. There is no vault-wide grant. A proxy identity is created in P2 only if proxy routing is chosen and has no Key Vault access.

The Event Hubs Standard tier has no platform `ConsumerLag` metric. Once Track D exports the Analytics app's own `shortener.analytics.consumer_lag` metric, add an alert on that metric for lag above 1,000 for 10 minutes; do not create an Event Hubs platform lag alert. The app 5xx, restart, and outbox alerts likewise require app/log telemetry in P2/P3. Do not count them as active P1 coverage. See the validation record for the phase boundary.

## Later phases

- P2: author the Container Apps environment, routing, Redis, API, Web, Analytics, and migration job; preview and obtain separate approval. Use prebuilt images by digest from `scripts/build-images.sh`. Keep `enableScaleOutSettings=false` until Tracks A and B merge. API and migration job receive `AzureAd__ClientId` from `API_CLIENT_ID`, each with its own identity. Web runs `Identity__Mode=Workforce` with `WEB_CLIENT_ID`. Microsoft documents `SignedAssertionFromManagedIdentity` for Microsoft.Identity.Web and multitenant app registration trust when the managed identity and app have the same home tenant. Once `id-shortener-web` exists, Michael must add a federated credential to the Web registration with its exact principal ID, home-tenant issuer, and `api://AzureADTokenExchange` audience; then P2 can configure Web's client credentials and validate live sign-in. Until that operator step and live proof, Web uses the Key Vault `web-client-secret`. Redis uses its own managed identity for the raw password and an Azure Files mount.
- P3: after Tracks A and B merge, set `Kafka__EnableIdempotence=true`, enable scale-out settings, and run the approved anonymous redirect scale demonstration against one managed demo link. It must measure replica and consumer-lag movement without directly editing an application database.

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
