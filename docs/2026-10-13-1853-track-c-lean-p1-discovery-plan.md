# Track C lean P1 discovery and preview plan — 2026-10-13 18:53 CDT

## Purpose and scope

Prepare a reviewable P1 foundation-and-data what-if for the single `prod` environment in `rg-schwab-url-shortener`. Stop before provider registration, resource-group creation, or any other provisioning until Michael approves P1. Never recreate `rg-shortener-staging`.

## Discovery completed

- Worktree `/Users/michaelinman/Repos/SchwabUrlShortener-infra`, branch `codex/azure-infra`, was clean before the 18:50 contract and handoff were recopied and committed. Both `rg-shortener-staging` and `rg-schwab-url-shortener` returned absent in read-only Azure checks.
- The signed-in account and subscription are the contract's `social@mediaraq.com` and `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`. Subscription policies identify an enabled free trial with a spending limit; the trial end date is not exposed by the checked CLI response and needs a billing-portal check.
- The P1 providers are registered except `Microsoft.EventHub` and `Microsoft.DBforPostgreSQL`. Do not register them before P1 approval.
- Central US capabilities list PostgreSQL `Standard_B1ms`.
- [Microsoft's PostgreSQL connection limits](https://learn.microsoft.com/en-us/azure/postgresql/configure-maintain/concepts-limits) list B1ms at 50 total connections and 35 user connections. The 18:50 contract's proposed nine app replicas × pool size 5 = 45 app connections exceeds 35 user connections. With ten user connections reserved as headroom, pool size 2 yields 18 app connections, below 25. Michael's contract correction or SKU decision is pending; do not author P1 connection strings or claim a valid P1 preview until resolved.
- [Key Vault](https://learn.microsoft.com/en-us/azure/key-vault/general/overview-vnet-service-endpoints), [Storage](https://learn.microsoft.com/en-us/azure/storage/common/storage-network-security-virtual-networks), and [Event Hubs Standard](https://learn.microsoft.com/en-us/azure/event-hubs/network-security) document subnet service-endpoint network rules. [Container Apps TCP ingress](https://learn.microsoft.com/en-us/azure/container-apps/ingress) explicitly supports Redis. [Container Apps route configuration](https://learn.microsoft.com/en-us/azure/container-apps/rule-based-routing) is documented with a preview Bicep API; its GA status, WebSockets, and sticky-session interaction are not confirmed, so P2 routing still requires review.

## Remaining tasks before P1 approval request

1. Resolve and record the B1ms connection-budget correction in the authoritative contract.
2. Revise P1 Bicep and parameters for the lean names, network, Basic ACR, capped logging, service-endpoint network rules, Burstable PostgreSQL, Event Hubs, Key Vault secrets, identities, storage, and $100 budget. Preserve the existing local staging password file while copying required values privately into `prod`; the user's instruction forbids deleting local passwords.
3. Build and lint Bicep, then run read-only subscription/resource what-if for the new resource group and P1 resources. Record all predicted creates, updates, and deletes, and any provider-registration limitation.
4. Present the exact P1 preview to Michael and ask for approval. No P1 deployment or provider registration precedes that approval.

## Acceptance criteria

- P1 plan matches the corrected single-environment contract and contains no secret values.
- Offline checks pass and what-if is reviewable, with no action against an existing resource group.
- Michael can approve or reject a concrete P1 resource list and known costs before any provisioning.

## Outcome — 2026-10-13 19:05 CDT

The 19:05 contract corrected the connection budget. P1 was authored and locally validated for the new group; the prior staging templates were removed from the active infrastructure tree while remaining in Git history. The local staging azd folder was retained. The three values required by the user were copied privately to `prod`; a separate Redis password was generated privately. No resource provider was registered and no Azure resource was created in this preparation step.

The P1 what-if and exact limitations are recorded in the dated validation section of `2026-10-13-1757-track-c-azure-infrastructure-validation.md`. One contract discrepancy needs resolution before claiming the full monitoring promise: the Event Hubs Standard tier does not expose the platform `ConsumerLag` metric; Microsoft documents those application metrics only on Premium and Dedicated. App 5xx, restart, and outbox alerts likewise depend on P2 apps or their telemetry. P1 contains only the feasible PostgreSQL alerts and an action group, with remaining alerts tracked for P2/P3.

## Latest outcome for the contract marked 19:20 CDT — 2026-10-13

The authoritative contract now fixes Event Hubs at one throughput unit and adds a Web client secret and separate service identities. P1 Bicep was updated accordingly. The final subscription-scope what-if predicts one new group plus 61 creates, with no updates, deletes, or unresolved resources. The migration identity has only ACR pull and its individual `pg-migrate-connection` secret grant. The full inventory, contract discrepancy about Standard-tier consumer lag, and pending `WEB_CLIENT_SECRET` entry are in the validation record. Provider registration and provisioning still await Michael's P1 approval.
