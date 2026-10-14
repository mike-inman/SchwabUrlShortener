# Track C Azure infrastructure validation — 2026-10-13 17:57 CDT (updated for 19:20 contract)

The earlier staging sections and initial P1 previews below are historical. The **current single-environment P1 state** is in the final 20:10 completion section. The staging group was deleted with Michael's approval; never recreate it.

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

## 19:35 Well-Architected baseline preview — 2026-10-13 (current)

The 19:35 contract, handoff, and Well-Architected review were recopied verbatim from the canonical checkout and committed as `8098573` before implementation. The local `WEB_CLIENT_SECRET` in azd `prod` was confirmed present by command exit status only; its value was not read or displayed. Michael described it as a dedicated Azure Web registration secret expiring in 180 days. The user-supplied trial record gives an approximate expiration of **2026-10-30**; the exact billing deadline remains a portal check. No subscription provider was registered and no resource was provisioned.

P1 Bicep now sets all five contract tags on every taggable top-level resource and on the new group. Several child resource schemas, role assignments, diagnostic settings, budgets, and the lock have no `tags` field; their taggable parent is tagged. Storage explicitly requires HTTPS, TLS 1.2, and disables public blobs. Event Hubs already had TLS 1.2; PostgreSQL now sets `ssl_min_protocol_version=TLSv1.2` and `require_secure_transport=ON`. ACR explicitly disables admin access and anonymous pull. Key Vault client TLS must be verified in P2 because the vault Bicep schema does not offer a minimum-TLS property. Private PostgreSQL network settings and `Maximum Pool Size=2` remain as in the prior preview. The 23-of-35 connection budget is unchanged.

The separate subscription template selects `Microsoft.Security/pricings/CloudPosture` at `Free`, the foundational CSPM plan; it does not enable a paid Defender tier. Read-only provider metadata showed `Microsoft.Security`, `Microsoft.EventHub`, and `Microsoft.DBforPostgreSQL` as `NotRegistered`. A read-only `az security pricing list` returned `Subscription Not Registered`, so the live secure score and recommendations cannot be recorded before approved provider registration. This is an outstanding P1 post-provision check. The `CanNotDelete` lock is a separate resource-group template and the combined preview gives it an explicit dependency on the other P1 resources and free posture setting. In the actual approved deployment, apply the lock **last**, after validation.

The Bicep workbook contains panels for requests, p95 latency, HTTP 5xx, replicas, Analytics consumer lag, outbox age, and PostgreSQL CPU/connections. It parsed as JSON and compiled into the template, but no panel has been exercised against live telemetry. The request/lag/outbox panels depend on Track D export, and replica data on P2 Container Apps diagnostics. The Event Hubs Standard platform has no suitable consumer-lag metric. Once Track D exports the Analytics app's own `shortener.analytics.consumer_lag`, add the `>1,000` for 10 minutes alert against that telemetry; no nonfunctional P1 lag alert was added.

Microsoft documents [managed-identity signed assertions in Microsoft.Identity.Web](https://learn.microsoft.com/en-us/entra/msidweb/authentication/certificateless) and [app-registration trust of a managed identity](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity). The latter supports a multitenant app when the user-assigned identity and registration share the home tenant, with the registration provisioned in other tenants. A read-only Entra lookup identified the existing SchwabUrlShortener Web registration as multitenant (`AzureADMultipleOrgs`), application ID `d4cd63a0-5583-41ad-a640-803ea76bb3a8`; the Web project uses Microsoft.Identity.Web 4.16.0. In P2, after `id-shortener-web` is created, Michael must create the federated credential on that registration using its exact principal ID, the home-tenant issuer, and audience `api://AzureADTokenExchange`; Track C then changes Web configuration and validates live sign-in. No federated credential or provider setting was changed in this P1 slice. The Key Vault Web secret remains the prepared fallback until federation is proven.

Read-only subscription-scope what-if with disposable placeholder passwords and Web secret, `infra/p1-preview.bicep`, and `ResourceIdOnly` output predicted **66 creates: one new `rg-schwab-url-shortener`, one subscription-level free `CloudPosture` setting, and 64 resources inside the new group; zero updates and zero deletes**. The 64 include PostgreSQL's two TLS configurations, the shared workbook, and the final lock. What-if is a control-plane prediction, not provider registration, deployment, network validation, RBAC proof, rendered workbook evidence, secure-score evidence, or live recovery test. It showed no existing resource group as a target. The provider-registration gap and the PostgreSQL private-access creation choice require another review immediately before any approved P1 provision.

Three dated runbooks cover private PostgreSQL point-in-time recovery, previous-digest rollback, and gradual secret rotation. They are procedures only and were not exercised. The P1 deployment order and limitations are in `infra/README.md`. P2 Web federation and Track D lag telemetry remain separate work; no application or database was modified.

## 19:50 least-privilege correction and P1 deployment — 2026-10-13

The 19:50 contract was recopied from the canonical checkout. It removes `id-shortener-api` from the `eventhubs-connection` secret readers because API writes to the PostgreSQL outbox and does not use Kafka. `infra/modules/data.bicep` now grants that secret only to `id-shortener-analytics`. The Analytics assignment kept its original `reader-1` name. Main and preview Bicep built and linted without warnings. The read-only what-if predicted **65 creates, zero updates, zero deletes**: the same new resource group, free `CloudPosture` setting, and P1 resources as the 19:35 preview, except for the API's single removed secret-scope role assignment. Its omitted role-assignment ID was `1a32bba5-e873-5220-a135-7fff1b26be6f`; the Analytics assignment ID remains `22869997-e59a-5c43-84a1-0417fdab3256`. Michael's P1 approval is conditional on exactly this difference and applies only to the previewed resources, with the lock last.

### Contract alert map

All authored P1 rules send to `ag-shortener-ops`, whose email receiver is `BUDGET_CONTACT_EMAIL`. A Bicep rule is not live coverage until its deployment and metric evaluation are verified.

| Contract condition | Rule/source | P1 status |
| --- | --- | --- |
| API or Web HTTP 5xx above 5% for 5 minutes | P2/Track D request telemetry; rule to be authored after the apps emit requests | Deferred; neither app nor request metric exists in P1 |
| Any app restarting repeatedly | P2 Container Apps restart telemetry; rule to be authored after apps exist | Deferred; no app exists in P1 |
| Analytics consumer lag above 1,000 for 10 minutes | Analytics app's own `shortener.analytics.consumer_lag` export and a later alert on that metric | Deferred to Track D export and P2/P3 alert; Event Hubs Standard has no suitable platform lag metric |
| Oldest unpublished outbox event above 5 minutes | Existing `shortener.clicks.outbox.oldest_seconds` app metric if Track D exports it, otherwise an application log query; later alert | Deferred to Track D export and P2/P3 alert |
| PostgreSQL CPU above 80% for 10 minutes | `alert-shortener-postgres-cpu`, `cpu_percent`, average, 10-minute window, evaluated each minute | Authored P1; **not deployed** because the data module failed |
| PostgreSQL connections above 80% of 35 for 10 minutes | `alert-shortener-postgres-connections`, `active_connections`, maximum greater than 28, 10-minute window, evaluated each minute | Authored P1; **not deployed**; server parameter `max_connections=50` includes reserved slots, while contract budgets 35 user slots |
| Event Hubs ingress above 70% of 1 TU for 10 minutes by bytes | `alert-shortener-eventhubs-ingress-bytes`, `IncomingBytes` total greater than 420,000,000 in 10 minutes, evaluated each minute | Authored P1; **not deployed** because the data module failed |
| Event Hubs ingress above 70% of 1 TU for 10 minutes by events | `alert-shortener-eventhubs-ingress-messages`, `IncomingMessages` total greater than 420,000 in 10 minutes, evaluated each minute | Authored P1; **not deployed** because the data module failed |

The budget notifications are separate from these operational rules: `budget-shortener` alerts at 50%, 80%, and 100% of its USD 100 monthly amount. The Container Apps environment diagnostic settings and deferred alert rules remain P2/Track D work.

### Approved P1 attempt and current Azure state

After the 65-create preview, Michael approved only those previewed resources and instructed that the lock be applied last. Read-only checks reconfirmed subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, tenant `eea269ab-1571-48a5-a18c-824ba5859cd5`, account `social@mediaraq.com`, and absence of `rg-schwab-url-shortener`. `Microsoft.Security`, `Microsoft.EventHub`, and `Microsoft.DBforPostgreSQL` were registered under that approval. The new tagged `rg-schwab-url-shortener` was created with `infra/resource-group.bicep`; no existing resource group was targeted. `azd provision -e prod --no-prompt` used the private local azd values with CLI output suppressed. It returned exit 1. The root deployment shows `network`, `observability`, `foundation`, and `budget-shortener` succeeded; `data` failed, so `monitoring` did not start.

The two failed operations were Key Vault keys `dataprotection-api` and `dataprotection-web`, both `ForbiddenByFirewall`. Azure reported that its `KeyVault/ManagementPlane` caller was denied because the vault's network ACL has `bypass: None`. The vault still has `defaultAction: Deny`, `bypass: None`; no network rule was changed. The earlier assumption that ARM deployment of keys would pass this firewall configuration is disproven for this deployment. [Microsoft's Key Vault trusted-services guidance](https://learn.microsoft.com/en-us/azure/key-vault/general/overview-vnet-service-endpoints) lists Azure Resource Manager template deployment among supported uses when the trusted-services bypass is enabled. Changing to `AzureServices` or using another key-creation path would need a revised, previewed decision; neither was attempted.

Successful data operations include the PostgreSQL server/database, Event Hubs namespace/topic/consumer group, both storage accounts and the Redis file share, six named Key Vault secrets, secret-scope RBAC grants, and PostgreSQL/Event Hubs/Key Vault diagnostic settings. PostgreSQL reports `Ready`, `publicNetworkAccess: Disabled`, the planned delegated subnet and private DNS zone, `ssl_min_protocol_version=TLSv1.2`, and `require_secure_transport=ON`. Its reported `max_connections` parameter is 50; the contract's 35-user-slot budget still needs a live reserved-slot check from an approved in-network client, without changing an application database. An exact-scope RBAC query for `eventhubs-connection` found one principal, Analytics; API is absent. The API's removed role-assignment ID was not created.

The separate approved `subscription-foundational-cspm.bicep` deployment succeeded and `CloudPosture` reports `Free`. `az security secure-scores list` returned no score yet, so posture recommendations and score cannot be reported as validated. The four operational metric alerts, action group, workbook, and two key-specific RBAC grants remain undeployed. The resource-group `CanNotDelete` lock was **not applied** because P1 is incomplete. No Container Apps, migrations, application database changes, or P2 Key Vault-reference checks were attempted. The secret values were never displayed or committed. Stop here and obtain a revised network/key-creation decision before retrying the incomplete P1 deployment; re-preview the delta first.

## 20:10 contract completion — 2026-10-13 (current)

Michael approved changing **only** this vault's `networkAcls.bypass` from `None` to `AzureServices` while preserving `defaultAction: Deny` and the Container Apps subnet rule. The 20:10 contract was recopied from the canonical checkout. `infra/modules/foundation.bicep` now declares that value for repeatability. The focused `infra/p1-vault-network.bicep` what-if showed **one Modify** with a single delta path, `properties.networkAcls.bypass`; no other resource change. That targeted deployment succeeded. A live read shows `AzureServices`, `Deny`, one allowed subnet, and the existing one operator IP rule. The operator IP rule remains because Michael authorized no other network change in this revision.

Azure's full azd preview initially failed on the existing `bypass: None` firewall during key preflight. Once bypass was changed, azd preview succeeded but reported default-property noise on existing ACR, PostgreSQL, Event Hubs, Application Insights, and VNet resources. Therefore the full template was **not redeployed**. A narrow `infra/p1-resume.bicep` references those existing resources and includes only the missing pieces. Its provider-backed what-if showed **10 Create, zero Modify/Delete**: two Data Protection keys, two key-scope Crypto User grants, `ag-shortener-ops`, four metric alerts, and the workbook. A separate final lock what-if showed **one Create, zero Modify/Delete**. These staged previews jointly represent the approved bypass delta and the incomplete P1 items. Both the resume and final lock deployments succeeded; the lock was applied last.

ARM management-plane GETs returned IDs for both `dataprotection-api` and `dataprotection-web` keys. The operator has no Key Vault key data-plane read grant, so no key material or versions were viewed. `az role assignment list --all` was compared against the contract's exact principal, role, and scope tuples for all five service identities: **17 expected, 17 actual, zero missing, zero extra** within this resource group. Breakdown: API 5 (ACR, two individual secrets, its key, blob container); Web 5 (ACR, two individual secrets, its key, blob container); Analytics 4 (ACR and three individual secrets); Migrate 2 (ACR and its individual PostgreSQL secret); Redis 1 (its individual password secret). The `eventhubs-connection` secret has one grant to Analytics and none to API; no identity has a vault-wide Key Vault role.

PostgreSQL reports `Ready`, `publicNetworkAccess: Disabled`, the planned delegated subnet and private DNS zone, `ssl_min_protocol_version=TLSv1.2`, and `require_secure_transport=ON`. The four P1 metric alerts are present and enabled, the action group exists, and the shared workbook ARM resource exists with the expected display name. The `lock-shortener-cannot-delete` lock is present at `CanNotDelete` on `rg-schwab-url-shortener`. `CloudPosture` remains `Free`. The secure-score query still returns an empty list; record the score and scoped free recommendations when Azure produces an assessment. No P2 app, migration, application database, or live alert firing was tested.

### Current contract alert coverage

| Contract condition | Implementing rule or dependency | Current state |
| --- | --- | --- |
| API or Web 5xx above 5% for 5 minutes | Request telemetry and alert to be added after P2 apps and Track D export | Deferred; no app request metric in P1 |
| Any app restarting repeatedly | Container Apps restart telemetry and alert after P2 | Deferred; no apps in P1 |
| Analytics consumer lag above 1,000 for 10 minutes | Alert on Analytics' own `shortener.analytics.consumer_lag` after Track D exports it | Deferred; Event Hubs Standard has no platform lag metric |
| Oldest unpublished outbox event above 5 minutes | Alert on exported `shortener.clicks.outbox.oldest_seconds`, or its application log source | Deferred to Track D and later alert |
| PostgreSQL CPU above 80% for 10 minutes | `alert-shortener-postgres-cpu` (`cpu_percent`, Average, 10 minutes) | Deployed and enabled; firing untested |
| PostgreSQL connections above 80% of 35 user slots for 10 minutes | `alert-shortener-postgres-connections` (`active_connections`, Maximum >28, 10 minutes) | Deployed and enabled; firing untested |
| Event Hubs ingress above 70% of one TU for 10 minutes, bytes | `alert-shortener-eventhubs-ingress-bytes` (`IncomingBytes` Total >420,000,000 per 10 minutes) | Deployed and enabled; firing untested |
| Event Hubs ingress above 70% of one TU for 10 minutes, events | `alert-shortener-eventhubs-ingress-messages` (`IncomingMessages` Total >420,000 per 10 minutes) | Deployed and enabled; firing untested |

The shared `ag-shortener-ops` action group uses `BUDGET_CONTACT_EMAIL`. The separate USD 100 monthly budget was deployed in the first attempt and contains 50/80/100% notifications. No alert delivery test has been run. P2 must verify that Container Apps resolve Key Vault references under the current subnet rule and `AzureServices` bypass before proposing any further network change. The initial full `azd` deployment retains a historical Failed record even though the focused resume deployments completed the P1 resource set.

## P2 preparation and subscription blockers — 2026-10-13 21:45 CDT

Michael approved P2 conditionally on a what-if showing only the new Container Apps resources in `rg-schwab-url-shortener`. `origin/main` is `912ac3b2090c7267b6aa8a295877b643b3eab729`; it is an ancestor of the Track C branch, and `Dockerfile` plus `src/` match it exactly. The P2 template and dated plan were committed as `7f9fa85`. `infra/scripts/build-images.sh` now accepts `IMAGE_SOURCE_REF` so the intended three `az acr build --target` calls use precisely that source commit. The nonsecret ACR name was added to the local `prod` azd environment.

The first ACR build was rejected before any image was created: `TasksOperationsNotAllowed` for `acrshortenerf2xpl6` in subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`. A registry repository listing returned empty. The three `SERVICE_*_IMAGE` azd values remain `bootstrap-unset`. No alternate local build/push was attempted because the contract requires ACR Tasks builds.

A provider-backed P2 what-if also failed at Container Apps environment preflight with `MaxNumberOfGlobalEnvironmentsInSubExceeded`: Azure says this subscription cannot have more than one Container Apps environment. Read-only listing found the existing `ae-workers` environment in unrelated `rg-agentengineering-workers-dev`, East US 2. It was not touched. No new P2 resource was deployed.

A second what-if with `--validation-level Template` succeeded. It used conspicuous `preview-only` placeholder image digests because no real images exist; **it is a scope check, not a deployable or provider-approved preview**. It predicted eight Creates, all inside `rg-schwab-url-shortener`: `cae-shortener`, its `redis-data` Azure Files storage binding and `routeshortener` route, `ca-shortener-redis`, `ca-shortener-api`, `ca-shortener-web`, `ca-shortener-analytics`, and `caj-shortener-migrate`. Existing P1 resources were `Ignore`; there were no Modify or Delete changes. The Bicep compiled, but provider input validation and runtime behavior remain unproven. The pinned public Redis image in the preview is `docker.io/library/redis@sha256:858f009f9709ce576febc734aa78b8f6d624b82571f9ddb6bda4377c833b3499`.

P2 deployment is blocked until Azure permits a new Container Apps environment in this subscription and permits ACR Tasks, or Michael authorizes a revised subscription or build approach. After the blockers are resolved, build real images, rerun the provider-backed what-if with their digests, and apply the existing P2 approval only if it shows exactly the approved resource set. Key Vault references, migration exit, app health, public smoke checks, Redis restart persistence, Analytics/Event Hubs health, host/protocol forwarding, and operator-IP removal remain untested. No Key Vault or other network rule was changed. The public host and exact Web registration redirect URIs cannot be known until the environment and route exist.

## P2 real-image preview and migration gate — 2026-10-13 22:25 CDT

See [the dated migration extension review](2026-10-13-2225-track-c-p2-migration-extension-review.md) for exact ACR digest verification, the provider-backed eight-Create what-if, approved deployment result, and the failed first migration execution. The sole proposed correction is a focused one-Modify what-if for PostgreSQL `azure.extensions=citext`; it has not been deployed. P2 public and runtime checks remain gated on migration exit 0.

## Entra as code addendum — 2026-10-13 23:07 CDT

The [dated Entra reconciliation validation](2026-10-13-2307-track-c-entra-reconciliation-validation.md) records the committed configuration, first no-op live Graph dry run, simulated custom-domain additions, optional federated-credential preview, and remaining domain/credential gates. No Entra mutation or P3 work was performed in this addendum slice.
