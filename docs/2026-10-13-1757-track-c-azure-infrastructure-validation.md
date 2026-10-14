# Track C Azure infrastructure validation — 2026-10-13 17:57 CDT (updated for 19:20 contract)

The earlier staging sections and initial P1 preview below are historical. The **current single-environment P1 state** is in the final dated section. The staging group was deleted with Michael's approval; never recreate it.

## State and boundary

- Worktree: `/Users/michaelinman/Repos/SchwabUrlShortener-infra`, branch `codex/azure-infra`, based on `origin/main` `66b8318a351788d6cec5ba6e3bfa3f0d0ed63ba0`.
- Corrected contract and handoff copied from the canonical checkout and committed first. The 17:58 subscription amendment was committed separately; its handoff copy was unchanged.
- Azure CLI read-only check: `Azure subscription 1`, subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, tenant `eea269ab-1571-48a5-a18c-824ba5859cd5`, account `social@mediaraq.com` match Michael's confirmation.
- Read-only `az group exists --name rg-shortener-staging` returned `false` at 17:57 CDT. This is a point-in-time result; recheck immediately before any creation. No resource group, application database, container, or other Azure resource was modified.
- Read-only 18:10 checks reconfirmed the account/subscription/tenant and `rg-shortener-staging` absence. The signed-in account has `Owner` at this subscription scope. The PostgreSQL `Standard_D2ds_v5` General Purpose SKU appears in `centralus` capabilities. Azure provider metadata lists `centralus` for Container Apps managed environments and Event Hubs namespaces. Managed Redis B0/B1 regional eligibility still needs a live SKU check during the portal walkthrough; provider location metadata alone is insufficient.
- The existing resource groups named in the contract remain outside this work. Do not modify or reuse them.
- At 18:17 CDT, Michael supplied `BUDGET_CONTACT_EMAIL=social@mediaraq.com` and the existing Entra API application ID. Created local azd environment `staging` for the confirmed subscription, `centralus`, and `rg-shortener-staging`, and set the non-secret foundation values, including `API_CLIENT_ID`, the $600 budget contact, `Balanced_B0`, and bootstrap image sentinels. Targeted `azd env get-value` checks confirmed these non-secret values. The local azd `.env` is mode 0600 and Git-ignored; `POSTGRES_PASSWORD` is still unset pending Michael's private entry. The staging resource group still does not exist, and no Azure resource was created.

## Authored inventory

| Phase | Resources in Bicep | Status |
| --- | --- | --- |
| C1 | VNet with `10.60.0.0/23` ACA and `10.60.4.0/24` private endpoint subnets; Log Analytics, Application Insights, ACR Standard, Key Vault, three user-assigned identities, ACR pull assignments, $600 budget with 50/80/100% alerts | Deployed and checked |
| C2 | PostgreSQL 17 GP D2ds_v5 with PgBouncer, `shortener` database; Managed Redis Balanced B0/B1; Event Hubs Standard with `managed-clicks-v2` and `shortener-analytics-v1`; storage container and Data Protection keys; Key Vault connection secrets; private endpoints/DNS and diagnostics | Authored; blocked by Redis SKU availability |
| C3 | VNet-integrated Container Apps environment, API/Web/Analytics apps, manual migration job; digest images, probes, KEDA Kafka rule, sticky Web sessions, Key Vault references | Authored; no image build or deployment |
| C4 | Front Door Standard endpoint, API and Web origin groups/routes, optional custom domain, diagnostics | Authored; no deployment |

Resource names use the six-character `uniqueString(resourceGroup().id)` suffix for globally unique resources. The public host is only a deterministic **planned** `afd-shortener-stg-<suffix>.azurefd.net` value; no endpoint or public host exists yet.

## Offline evidence

| Check | Result |
| --- | --- |
| `az bicep build --file infra/main.bicep --stdout` | Pass; no errors or warnings |
| `az bicep lint --file infra/main.bicep` | Pass; no errors or warnings |
| `az bicep build` and `az bicep lint` for `infra/resource-group.bicep` | Pass; no errors or warnings |
| `POSTGRES_PASSWORD=offline-placeholder az bicep build-params --file infra/main.staging.bicepparam --stdout` | Pass with disposable nonsecret placeholder; output discarded |
| `sh -n infra/scripts/build-images.sh` | Pass |
| Ruby YAML parse of `azure.yaml` and JSON parse of `infra/main.parameters.json` | Pass |
| `git diff --check` | Pass |
| ACR build, azd image substitution, C3–C4 what-if, private DNS resolution for data services, migration, guest smoke, WebSocket, Entra sign-in | Not run; require C2 and later deployment gates |

Installed tools: Azure CLI 2.90.0, Bicep CLI 0.48.1, azd 1.35.0. No secrets were read or printed. The `POSTGRES_PASSWORD` placeholder used by offline compilation is not a real password and was not saved to a file.

## Deployment gates and commands

The 18:10 contract revision resolved the prior `Host` versus `AllowedHosts` mismatch. Each app's allowed-host list now contains its own `<app>.<environment defaultDomain>` origin FQDN. Bicep reads `defaultDomain` from the Container Apps environment before creating apps, while Front Door still sends the corresponding origin FQDN as `Host`. This matches [Front Door's origin host behavior](https://learn.microsoft.com/en-us/azure/frontdoor/front-door-backend-pool) and [ASP.NET Core host filtering](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/security-considerations). Live request and probe behavior remains unverified until C3–C4.

The ACR build script now constructs a temporary build context from committed `Dockerfile` and `src` files using `git archive`. This keeps the local `.azure` environment directory, including operator-entered values, out of remote build uploads. It also rejects uncommitted and untracked work before using a Git commit as the image tag.

Before provisioning, recheck the subscription/account/tenant and preview each phase. The resource group creation itself needed a subscription-scope preview because a group-level what-if could not run before the group existed. The repeated resource-group what-if reported **one create: `resourceGroups/rg-shortener-staging`**, and no update/delete. The group was created only after another absence check. C1 foundation what-if reported 17 creates in that group and no update/delete; `azd provision` succeeded. No pre-existing resource group was touched.

| Phase | Preview and validation after a future provision | Result now |
| --- | --- | --- |
| C1 | RG creation and foundation what-if, then provision; inspect VNet, ACR admin disabled, Key Vault private access/RBAC, identities, $600 budget | Succeeded. ACR Standard/admin disabled; vault RBAC, purge protection, public access disabled; vault private endpoint approved; $600 monthly budget present. Foundation deployments succeeded. |
| C2 | `azd provision --preview` with `DEPLOYMENT_PHASE=data`; inspect private access and private DNS/IP for PostgreSQL, Redis, Event Hubs, Storage, Key Vault; check only Key Vault secret names | azd preview passed. Full what-if returned Azure `InternalServerError` twice. Isolated data-module what-if reported `CacheCapacityUnavailable` for B0 and B1 in `centralus`. Isolated `eastus2` SKU what-ifs reported that this subscription cannot create either B0 or B1 there. No data resources deployed. |
| C3 | `infra/scripts/build-images.sh`, then `azd provision --preview` with `DEPLOYMENT_PHASE=apps`; run migration once, verify API and Web health and Analytics zero replicas | Not run |
| C4 | `azd provision --preview` with `DEPLOYMENT_PHASE=edge`; check route and origin configuration, then smoke `GET /`, `GET /health/ready`, guest create 201, redirect 302, and WebSocket upgrade | Not run |

At 18:32 CDT, Michael relayed Track A's confirmation from Microsoft documentation that Event Hubs supports idempotent Kafka producers. The scale-out setting is now `Kafka__EnableIdempotence=true` for the API and Analytics, still gated by `enableScaleOutSettings=false` for today's images. Microsoft's [Kafka protocol overview](https://learn.microsoft.com/en-us/azure/event-hubs/azure-event-hubs-apache-kafka-overview) confirms this support.

The production follow-ups remain multi-region, zone redundancy, Front Door Premium private origins/WAF tuning, Azure SignalR, PostgreSQL Entra auth, Event Hubs OAuth, and load/failure testing. Sign-in also awaits redirect URI and Track A forwarded-header work; Analytics and multi-replica tests await Track A; live updates await Track B.

## Cost and operator actions

**Provisional planning range: USD $300–$600/month** assumed two continuously warm 0.5 vCPU/1 GiB apps, one PostgreSQL D2ds_v5 server with 64 GiB storage, two-node Redis B0, one Event Hubs Standard TU, ACR Standard, five private endpoints, modest logs and traffic, and Front Door Standard. This estimate is no longer actionable while B0/B1 are unavailable to the subscription in both contract regions. The $600 budget exists and sends alerts at $300, $480, and $600; it is not a spending cap. Any SKU or region change needs a revised cost estimate before C2. See the [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/), [Front Door pricing](https://azure.microsoft.com/en-us/pricing/details/frontdoor/), [PostgreSQL pricing](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/), [Managed Redis pricing](https://azure.microsoft.com/en-us/pricing/details/managed-redis/), and [Event Hubs pricing](https://azure.microsoft.com/en-us/pricing/details/event-hubs/).

The immediate operator action is to resolve Managed Redis Balanced B0/B1 capacity or subscription access, or explicitly revise the SKU/region and budget contract. `POSTGRES_PASSWORD` was entered privately and remains in the local azd environment; its value was never displayed. The local deployment phase was returned to `foundation` after the failed previews to prevent an accidental data provision; no C2 resources were created. After an approved resolution: rerun data what-if, review the cost estimate, and proceed through C2–C4 gates; optionally add staging domain/DNS and Entra redirect URIs; validate the guest/WebSocket flow. `API_CLIENT_ID` feeds `AzureAd__ClientId` on the API and migration job. No provider configuration or customer onboarding implementation belongs in this track.

## Documentation checked

- [azd schema: prebuilt Container Apps images and predeploy hooks](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/azd-schema); [azd environment substitution](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/manage-environment-variables); [ACR build target and build arguments](https://learn.microsoft.com/en-us/cli/azure/acr).
- [PostgreSQL PgBouncer](https://learn.microsoft.com/en-us/azure/postgresql/connectivity/concepts-pgbouncer); [PostgreSQL private endpoint](https://learn.microsoft.com/en-us/azure/postgresql/network/how-to-networking-servers-deployed-public-access-add-private-endpoint); [Managed Redis Bicep resource](https://learn.microsoft.com/en-us/azure/templates/microsoft.cache/2025-07-01/redisenterprise); [Managed Redis database Bicep resource](https://learn.microsoft.com/en-us/azure/templates/microsoft.cache/2025-07-01/redisenterprise/databases); [Managed Redis private link](https://learn.microsoft.com/en-us/azure/redis/private-link).
- [Event Hubs tier/private link comparison](https://learn.microsoft.com/en-us/azure/event-hubs/compare-tiers); [Container Apps scaling](https://learn.microsoft.com/en-us/azure/container-apps/scale-app); [KEDA Kafka scaler authentication](https://keda.sh/docs/2.21/scalers/apache-kafka/); [Container Apps sticky sessions](https://learn.microsoft.com/en-us/azure/container-apps/sticky-sessions); [Front Door WebSocket support](https://learn.microsoft.com/en-us/azure/frontdoor/standard-premium/websocket).

## Initial single-environment P1 preview — 2026-10-13 19:05 CDT (superseded)

### Scope and state

- Worktree: `/Users/michaelinman/Repos/SchwabUrlShortener-infra`, branch `codex/azure-infra`. The authoritative contract was recopied at its 19:05 revision and handoff at 19:00, and committed as `821d73b`. This section supersedes the staging design above.
- Read-only account check matched subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, tenant `eea269ab-1571-48a5-a18c-824ba5859cd5`, account `social@mediaraq.com`, state Enabled. `rg-schwab-url-shortener` is still absent. No provider was registered and no Azure resource was created by this P1 preparation.
- `Microsoft.EventHub` and `Microsoft.DBforPostgreSQL` are still NotRegistered; the other P1 providers were previously read as Registered. Provider registration requires P1 approval.
- Local azd `prod` targets the confirmed subscription, `centralus`, and the new group. `POSTGRES_PASSWORD`, `BUDGET_CONTACT_EMAIL`, and `API_CLIENT_ID` were copied from local `staging` without displaying their values. `REDIS_PASSWORD` was generated privately. The local staging azd folder remains intact at Michael's request. No secret was printed, committed, or sent to Azure in the what-if; it used disposable placeholders.

### P1 Bicep and preview

- P1 contains the VNet `10.60.0.0/16`, ACA subnet `10.60.0.0/23` with Storage/KeyVault/EventHub service endpoints, delegated PostgreSQL subnet `10.60.2.0/24`, and linked private DNS zone. The compiled PostgreSQL resource has `Standard_B1ms`, version 17, `publicNetworkAccess: Disabled`, a delegated subnet and private DNS zone. This is offline template evidence, not a live network test.
- P1 also includes Log Analytics with 1 GB/day cap, workspace Application Insights, ACR Basic, RBAC Key Vault with purge protection, four managed identities including a dedicated Redis identity, separate Data Protection and Redis storage accounts, the `redis-data` Azure Files share, Data Protection keys, private PostgreSQL database, Event Hubs Standard and `managed-clicks-v2`, five named Key Vault secrets, a $100 monthly budget with 50/80/100% email alerts, diagnostics for PostgreSQL/Event Hubs/Key Vault, one action group, and PostgreSQL CPU/connections alerts.
- PostgreSQL app secret uses `Maximum Pool Size=2`. At planned maximum replicas: API `5×2=10`, Analytics `4×(2+1)=12` including its separate readiness-check pool, migration `1`; total **23 of 35 user connections**, leaving 12. The active-connections alert threshold is 28, which is 80% of 35. The actual server setting and reserved slots must be checked after provisioning. [Microsoft's limits](https://learn.microsoft.com/en-us/azure/postgresql/configure-maintain/concepts-limits) give B1ms 35 available user connections.
- `az bicep build` and `az bicep lint` passed for `main.bicep` and `p1-preview.bicep` with zero warnings; `resource-group.bicep` built cleanly. `main.prod.bicepparam` compiled with disposable placeholders, its output discarded. `git diff --check` passed.
- Read-only subscription-scope what-if used `infra/p1-preview.bicep` and disposable placeholder passwords. It predicted **one new resource group and 42 creates inside it, zero updates and zero deletes**. Thirteen secret/key/container RBAC assignments were marked `Unsupported` by what-if because managed identity principal IDs are known only during deployment; those assignments exist in Bicep but are not confirmed by preview. No existing group was targeted. The preview contained the private PostgreSQL resource; the compiled template check above confirms its no-public-access properties.

### Monitoring discrepancy and phase boundary

- The authoritative contract asks for Analytics `ConsumerLag >1,000` on Event Hubs Standard. [Microsoft's Event Hubs monitoring reference](https://learn.microsoft.com/en-us/azure/event-hubs/monitor-event-hubs-reference) says `ConsumerLag` application metric logs are only available on Premium/Dedicated, and the [Standard namespace metric list](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/supported-metrics/microsoft-eventhub-namespaces-metrics) has no `ConsumerLag` platform metric. A Standard namespace metric alert would never establish the required coverage, so it was removed from P1. A real lag source or tier change must be decided before calling that alert complete.
- API/Web 5xx and restart alerts require P2 Container Apps; outbox age requires an application log or exported metric. They are tracked for P2/P3 and are not part of the 42 P1 creates. Container Apps environment diagnostics also wait for the P2 environment. P1 has only the PostgreSQL CPU and connection rules, with the shared email action group.
- P2 routing, digest images, migration, API client wiring, Redis persistence restart test, WebSocket test, guest/managed link flow, and live Entra sign-in have not been run. P3's scale demo will use **anonymous redirects of one managed demo link**, record replica and lag movement, and never write link or click rows directly to the database.
- The free trial's end date was not exposed by the checked CLI and needs Michael's billing-portal check. A calculator-based monthly estimate is not yet recorded; the contract's USD 40–70 range is a target, not a verified quote. The USD 100 budget is an alert threshold, not a spend cap.

### Approval gate

P1 approval would authorize registering the two missing providers and creating resources **only** in the new `rg-schwab-url-shortener`, after a fresh account/group check and what-if. PostgreSQL private access is a creation-time choice. No P1 deployment has been performed.

## Final P1 preview for the contract marked 19:20 CDT — 2026-10-13

The 19:10, 19:15, and 19:20 contract/handoff amendments were recopied and committed separately as `d58596d` and `939e9d4`. This section supersedes the earlier 42-create preview. The local `prod` azd environment now also contains the nonsecret `WEB_CLIENT_ID`; `WEB_CLIENT_SECRET` is **pending Michael's hidden local entry**. The final what-if used a disposable placeholder for it and for the passwords; no real secret was submitted or displayed. P1 must not be provisioned with a placeholder.

The Bicep has Event Hubs Standard at **fixed 1 throughput unit, auto-inflate disabled**. Two independent 10-minute average ingress alerts cover 420,000,000 bytes and 420,000 messages per window, corresponding to roughly 0.7 MB/s and 700 events/s. The shared action group sends to the budget contact. [Azure's supported Event Hubs metrics](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/supported-metrics/microsoft-eventhub-namespaces-metrics) list `IncomingBytes` and `IncomingMessages` at one-minute granularity and Total aggregation. The Standard-tier consumer-lag discrepancy recorded above remains open for P2/P3.

P1 has **one identity per service**: API, Web, Analytics, migration job, and Redis. A proxy identity is conditional on a P2 routing decision. Static Bicep review shows these exact grants:

| Identity | ACR pull | Key Vault Secrets User, individual secret scopes | Other individual scopes |
| --- | --- | --- | --- |
| `id-shortener-api` | Yes | `pg-app-connection`, `redis-connection`, `eventhubs-connection` | Crypto User on `dataprotection-api`; Blob Data Contributor on `dataprotection` |
| `id-shortener-web` | Yes | `redis-connection`, `web-client-secret` | Crypto User on `dataprotection-web`; Blob Data Contributor on `dataprotection` |
| `id-shortener-analytics` | Yes | `pg-app-connection`, `redis-connection`, `eventhubs-connection` | None |
| `id-shortener-migrate` | Yes | `pg-migrate-connection` only | None |
| `id-shortener-redis` | No | `redis-password` only | None |

There is no vault-wide Key Vault role assignment. These are authored and predicted grants, not yet verified live. [Azure's RBAC guidance](https://learn.microsoft.com/en-us/azure/key-vault/general/rbac-guide) supports individual secret/key scopes; [Key Vault network guidance](https://learn.microsoft.com/en-us/azure/key-vault/general/how-to-azure-key-vault-network-security) confirms ARM deployment of secrets/keys uses the control plane and is not blocked by vault data-plane firewall rules.

`az bicep build` and `az bicep lint` for `main.bicep` and `p1-preview.bicep` passed with zero warnings. `main.prod.bicepparam` compiled with disposable placeholders; JSON parameters parsed and `git diff --check` passed. The subscription-scope what-if against the confirmed subscription predicted **one new `rg-schwab-url-shortener` and 61 creates in it, zero updates, zero deletes, and zero unsupported resources**. The preview includes five identities, `web-client-secret`, the two throughput alerts, the private PostgreSQL server, and its linked VNet/DNS. No resource was created or modified. The two missing providers remain unregistered until P1 approval. The `WEB_CLIENT_SECRET` local entry and explicit P1 approval remain before any registration or provisioning.
