# Track C Azure infrastructure validation — 2026-10-13 17:57 CDT

## State and boundary

- Worktree: `/Users/michaelinman/Repos/SchwabUrlShortener-infra`, branch `codex/azure-infra`, based on `origin/main` `66b8318a351788d6cec5ba6e3bfa3f0d0ed63ba0`.
- Corrected contract and handoff copied from the canonical checkout and committed first. The 17:58 subscription amendment was committed separately; its handoff copy was unchanged.
- Azure CLI read-only check: `Azure subscription 1`, subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, tenant `eea269ab-1571-48a5-a18c-824ba5859cd5`, account `social@mediaraq.com` match Michael's confirmation.
- Read-only `az group exists --name rg-shortener-staging` returned `false` at 17:57 CDT. This is a point-in-time result; recheck immediately before any creation. No resource group, application database, container, or other Azure resource was modified.
- The existing resource groups named in the contract remain outside this work. Do not modify or reuse them.

## Authored inventory

| Phase | Resources in Bicep | Status |
| --- | --- | --- |
| C1 | VNet with `10.60.0.0/23` ACA and `10.60.4.0/24` private endpoint subnets; Log Analytics, Application Insights, ACR Standard, Key Vault, three user-assigned identities, ACR pull assignments, optional $300 budget | Authored; no deployment |
| C2 | PostgreSQL 17 GP D2ds_v5 with PgBouncer, `shortener` database; Managed Redis Balanced B0/B1; Event Hubs Standard with `managed-clicks-v2` and `shortener-analytics-v1`; storage container and Data Protection keys; Key Vault connection secrets; private endpoints/DNS and diagnostics | Authored; no deployment |
| C3 | VNet-integrated Container Apps environment, API/Web/Analytics apps, manual migration job; digest images, probes, KEDA Kafka rule, sticky Web sessions, Key Vault references | Authored; no image build or deployment |
| C4 | Front Door Standard endpoint, API and Web origin groups/routes, optional custom domain, diagnostics | Authored; contract conflict blocks deployment |

Resource names use the six-character `uniqueString(resourceGroup().id)` suffix for globally unique resources. The public host is only a deterministic **planned** `afd-shortener-stg-<suffix>.azurefd.net` value; no endpoint or public host exists yet.

## Offline evidence

| Check | Result |
| --- | --- |
| `az bicep build --file infra/main.bicep --stdout` | Pass; no errors or warnings |
| `az bicep lint --file infra/main.bicep` | Pass; no errors or warnings |
| `POSTGRES_PASSWORD=offline-placeholder az bicep build-params --file infra/main.staging.bicepparam --stdout` | Pass with disposable nonsecret placeholder; output discarded |
| `sh -n infra/scripts/build-images.sh` | Pass |
| Ruby YAML parse of `azure.yaml` and JSON parse of `infra/main.parameters.json` | Pass |
| `git diff --check` | Pass |
| ACR build, azd image substitution, Azure what-if, SKU availability, private DNS resolution, migration, guest smoke, WebSocket, Entra sign-in | Not run; require staged deployment and operator inputs |

Installed tools: Azure CLI 2.90.0, Bicep CLI 0.48.1, azd 1.35.0. No secrets were read or printed. The `POSTGRES_PASSWORD` placeholder used by offline compilation is not a real password and was not saved to a file.

## Deployment gates and commands

The [handoff](2026-10-13-1720-track-c-azure-infrastructure-handoff.md) says to stop if the contract is wrong. The contract currently sets `AllowedHosts` for API to the public Front Door host and `ca-shortener-api-stg`, and for Web to only the public host. It also requires Front Door to send each Container App **origin FQDN** as its `Host` header. Those FQDNs are absent from `AllowedHosts`, so ASP.NET Core host filtering can reject Front Door probes and requests. This follows from [Front Door's origin host behavior](https://learn.microsoft.com/en-us/azure/frontdoor/front-door-backend-pool) and [ASP.NET Core host filtering](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/security-considerations). C4 requires a contract amendment: either add each origin FQDN to its app's `AllowedHosts`, or change the Front Door origin host header and validate TLS/origin routing. The current Bicep keeps the agreed values and makes no silent change.

Before any provisioning, recheck the subscription/account/tenant and that `rg-shortener-staging` is still absent. Preview resource group creation and each phase; inspect the what-if output before executing. The resource group creation itself needs a subscription-scope preview because a group-level what-if cannot run before the group exists. Do not run a command that touches an existing resource group. No what-if has been run and no summary exists yet.

| Phase | Preview and validation after a future provision | Result now |
| --- | --- | --- |
| C1 | Preview the new RG and foundation; inspect VNet, ACR admin disabled, Key Vault private access/RBAC, identities, budget only if contact email supplied | Not run |
| C2 | `azd provision --preview` with `DEPLOYMENT_PHASE=data`; inspect private access and private DNS/IP for PostgreSQL, Redis, Event Hubs, Storage, Key Vault; check only Key Vault secret names | Not run |
| C3 | `infra/scripts/build-images.sh`, then `azd provision --preview` with `DEPLOYMENT_PHASE=apps`; run migration once, verify API and Web health and Analytics zero replicas | Not run |
| C4 | After contract correction, `azd provision --preview` with `DEPLOYMENT_PHASE=edge`; check route and origin configuration, then smoke `GET /`, `GET /health/ready`, guest create 201, redirect 302, and WebSocket upgrade | Blocked by contract conflict |

The production follow-ups remain multi-region, zone redundancy, Front Door Premium private origins/WAF tuning, Azure SignalR, PostgreSQL Entra auth, Event Hubs OAuth, and load/failure testing. Sign-in also awaits redirect URI and Track A forwarded-header work; Analytics and multi-replica tests await Track A; live updates await Track B.

## Cost and operator actions

**Provisional planning range: USD $300–$600/month**, assuming two continuously warm 0.5 vCPU/1 GiB apps, one PostgreSQL D2ds_v5 server with 64 GiB storage, two-node Redis B0, one Event Hubs Standard TU, ACR Standard, five private endpoints, modest logs and traffic, and Front Door Standard. This is a directional allowance, not a calculator quote or a spending cap. Public price tables vary by region and offer and do not expose all values without an account-specific calculator selection. Front Door Standard alone lists a $35 monthly base fee, before traffic. The $300 budget may alert before a full month; Michael should review an account-specific [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/) estimate before C1. See [Front Door pricing](https://azure.microsoft.com/en-us/pricing/details/frontdoor/), [PostgreSQL pricing](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/), [Managed Redis pricing](https://azure.microsoft.com/en-us/pricing/details/managed-redis/), and [Event Hubs pricing](https://azure.microsoft.com/en-us/pricing/details/event-hubs/).

The remaining actions for a step-by-step walkthrough with Michael are: correct the origin-host contract; verify role access and centralus SKU availability; configure the azd staging environment and enter `POSTGRES_PASSWORD` without showing it; choose a budget email and review the cost estimate; provide the existing Entra API client ID; perform each preview and deployment gate; optionally add a staging domain and DNS validation; set staging Entra redirect URIs; and validate the guest/WebSocket flow. No provider configuration or customer onboarding implementation belongs in this track.

## Documentation checked

- [azd schema: prebuilt Container Apps images and predeploy hooks](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/azd-schema); [azd environment substitution](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/manage-environment-variables); [ACR build target and build arguments](https://learn.microsoft.com/en-us/cli/azure/acr).
- [PostgreSQL PgBouncer](https://learn.microsoft.com/en-us/azure/postgresql/connectivity/concepts-pgbouncer); [PostgreSQL private endpoint](https://learn.microsoft.com/en-us/azure/postgresql/network/how-to-networking-servers-deployed-public-access-add-private-endpoint); [Managed Redis Bicep resource](https://learn.microsoft.com/en-us/azure/templates/microsoft.cache/2025-07-01/redisenterprise); [Managed Redis database Bicep resource](https://learn.microsoft.com/en-us/azure/templates/microsoft.cache/2025-07-01/redisenterprise/databases); [Managed Redis private link](https://learn.microsoft.com/en-us/azure/redis/private-link).
- [Event Hubs tier/private link comparison](https://learn.microsoft.com/en-us/azure/event-hubs/compare-tiers); [Container Apps scaling](https://learn.microsoft.com/en-us/azure/container-apps/scale-app); [KEDA Kafka scaler authentication](https://keda.sh/docs/2.21/scalers/apache-kafka/); [Container Apps sticky sessions](https://learn.microsoft.com/en-us/azure/container-apps/sticky-sessions); [Front Door WebSocket support](https://learn.microsoft.com/en-us/azure/frontdoor/standard-premium/websocket).
