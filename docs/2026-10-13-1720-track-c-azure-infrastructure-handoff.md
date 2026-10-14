## Track C — Azure Lean Scalable Environment Handoff
Status: drafted on 2026-10-13; **rewritten 18:50 CDT** for one lean environment, replacing the staging design. **Revised 19:00 CDT**: PostgreSQL VNet integration, Redis persistence, monitoring, and automated Kafka-lag scale demo restored. **Revised 19:10 CDT**: design principles; Event Hubs fixed at 1 throughput unit with an alert, auto-inflate disabled. **Revised 19:15 CDT**: Entra identity settings and Web client secret in Key Vault. **Revised 19:20 CDT**: one identity per service with per-secret access.
You are working in /Users/michaelinman/Repos/SchwabUrlShortener-infra.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Recopy this handoff and `docs/2026-10-13-1720-azure-scaleout-contract.md` from `/Users/michaelinman/Repos/SchwabUrlShortener/docs/` into your worktree and commit them first as `docs: adopt lean single-environment contract`.
- Preserve all other worktrees. Edit only `infra/**`, `azure.yaml`, and your dated docs. Never edit `src/`, `tests/`, `Dockerfile`, or any `compose*.yaml`.
- Do not modify `.env*`, credentials, local agent configuration, the assignment PDF, or any local Docker container or volume.
- The contract is authoritative for names, sizes, network, settings, and routing. Stop and report if it is wrong.

What changed and why:
- Michael does not want a staging environment. There is exactly one environment, `rg-schwab-url-shortener`, sized small but configured to demonstrate scaling while evaluators use the app.
- The previous design priced at roughly USD 300–600/month; the target is roughly USD 40–70/month.
- Azure Managed Redis `Balanced_B0`/`B1` were unavailable (`CacheCapacityUnavailable` in `centralus`; not creatable in `eastus2`). The subscription is a free trial and `Microsoft.Cache` is not registered. Redis now runs as a container, so no Redis SKU is needed.
- Michael approved deleting `rg-shortener-staging`; it was deleted at 18:49 CDT. Never recreate it.
- The contract's **Design principles** section governs every choice: low expected load, elastic scale-out and automatic retraction, cost-conscious without sacrificing security or performance, every lean choice naming its upgrade, and demonstrable scaling. `infra/README.md` opens with these principles and maps each resource to them.
- Michael wants the environment to demonstrate good design principles. Low-cost controls stay in: PostgreSQL has no public endpoint, data services accept only the app subnet, Redis persists, and alerts are configured.

Deployment authority:
- Create and update resources only in `rg-schwab-url-shortener`, in subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`. Print `az account show` subscription ID and user before the first deployment and stop if either differs from the contract.
- Run a what-if (or `azd provision --preview`) before every provisioning run and summarize it in the validation note. Michael approves each phase in chat.
- Never delete a resource group, database, Key Vault, or storage account. Never purge Key Vault. Never touch any other resource group.

Task: Revise the authored Bicep for the contract's lean single environment, deploy it in three gated phases, and produce a repeatable, recorded scale demonstration.

Goal:
Evaluators reach one public HTTPS address where guest links, redirects, the signed-in dashboard, and live updates work. Every app has real autoscale rules, Web runs behind sticky sessions, Analytics scales on Kafka lag, and a scripted demonstration shows replicas scaling out under load and back in afterwards. Each lean choice documents its production upgrade step.

Important context:
- Your existing Bicep targets the old staging design. Reuse what fits (Container Apps, identities, Key Vault, storage, Event Hubs, registry, logging, budget, image build script, `azure.yaml`) and remove Front Door, private endpoints, private DNS zones, the private endpoint subnet, Managed Redis, PgBouncer, and every `-stg` name.
- The local `azd` environment `staging` holds `POSTGRES_PASSWORD`, `BUDGET_CONTACT_EMAIL`, and `API_CLIENT_ID`. Create `azd` environment `prod` and copy those three values from `staging` without displaying them, for example `azd env set POSTGRES_PASSWORD "$(azd env get-value POSTGRES_PASSWORD -e staging)" -e prod --no-prompt`. Then select `prod` and delete the local `.azure/staging` directory. Never print the password.
- Track A (fix in progress, then merge) adds the scale-out settings; Track B (fix in progress, rebase after A) adds live updates. Until both merge, deploy today's `main` images with `enableScaleOutSettings=false`.
- Track A confirmed `Kafka__EnableIdempotence=true` for Event Hubs.

In scope only:

P1 — Foundation and data (one approval)
1. Register resource providers the deployment needs if not registered (`Microsoft.App`, `Microsoft.OperationalInsights`, `Microsoft.ContainerRegistry`, `Microsoft.KeyVault`, `Microsoft.Storage`, `Microsoft.EventHub`, `Microsoft.DBforPostgreSQL`, `Microsoft.ManagedIdentity`, `Microsoft.Consumption`). Record which were registered.
2. Resource group `rg-schwab-url-shortener` in `centralus`.
3. VNet, the Container Apps subnet with service endpoints, and the delegated PostgreSQL subnet with its private DNS zone, exactly per the contract.
4. Log Analytics with a 1 GB/day cap, workspace-based Application Insights.
5. Container Registry Basic, admin user disabled.
6. Key Vault Standard, RBAC, soft delete and purge protection; network rules per the contract.
7. One user-assigned identity per service, exactly per the contract's Managed identities table (`id-shortener-api`, `-web`, `-analytics`, `-migrate`, `-redis`, and `-proxy` only if the proxy is used), with role assignments scoped to individual secrets, keys, and the blob container, never vault-wide. Record every assignment and its scope in the validation note.
8. Storage account for Data Protection keys and Key Vault keys `dataprotection-api` and `dataprotection-web`, per the contract.
9. PostgreSQL Flexible Server Burstable `Standard_B1ms`, PostgreSQL 17, 32 GiB, 7-day backups, **private access (VNet integration) in the delegated subnet, no public endpoint, no firewall rules**, TLS required, administrator `shortener`, database `shortener`. Read `max_connections` and record the connection-budget arithmetic from the contract. Private access cannot be changed after creation; verify it in the what-if before approval.
10. Event Hubs Standard per the contract, with a Send/Listen-only authorization rule and network rules per the contract.
11. Key Vault secrets `pg-app-connection`, `pg-migrate-connection`, `eventhubs-connection`, `redis-connection` and `redis-password` (from one generated Redis password; never printed), and `web-client-secret` from the secure `azd` value `WEB_CLIENT_SECRET`. Set `WEB_CLIENT_ID` in the `azd` environment yourself (it is not secret); ask Michael to set `WEB_CLIENT_SECRET` with a hidden-input command, as you did for the password. Apply the contract's Identity and Secret handling sections.
12. Budget USD 100/month with alerts at 50/80/100% to `BUDGET_CONTACT_EMAIL`.
12a. The separate Redis persistence storage account and Azure Files share `redis-data`, per the contract.
12b. Diagnostic settings, one action group, and the alert rules listed under Monitoring in the contract. Record each rule's name and threshold.

P2 — Apps with today's images (one approval)
13. Container Apps environment (consumption workload profile) in the subnet, logging to Log Analytics.
14. Public routing per the contract: verify native environment path-based routing (general availability in `centralus`, WebSockets, sticky sessions for Web) and record the documentation URL. Use it if all three hold; otherwise deploy `ca-shortener-proxy` with Caddy configured from `deploy/Caddyfile.local` plus the contract's `header_up Host` and trusted-proxy settings, delivered as a Container Apps secret or a config file mount, not a custom image.
15. `ca-shortener-redis`, using `id-shortener-redis`: `redis:7.4-alpine` pinned by digest, `--requirepass` from Key Vault secret `redis-password`, `--appendonly yes --appendfsync everysec --dir /data`, `/data` mounted from the `redis-data` Azure Files share through a Container Apps environment storage definition, internal TCP ingress on 6379, 1 replica. Verify that data survives a replica restart.
16. `ca-shortener-api`, `ca-shortener-web`, `ca-shortener-analytics` with the contract's CPU, memory, replica, scale-rule, probe, `AllowedHosts`, `PublicLinks__BaseUrl`, and Key Vault reference settings. Web: single revision mode, session affinity `sticky`. Analytics: KEDA `kafka` rule using the Event Hubs SASL secret.
17. `caj-shortener-migrate`, using `id-shortener-migrate`: manual job, API image, `--migrate`, `pg-migrate-connection`, 10-minute timeout.
18. Build and push images with `infra/scripts/build-images.sh`, run the migration job once, and confirm all apps healthy.
19. `enableScaleOutSettings` (default `false`) gates the settings only Track A's and Track B's code understands: `DataProtection__*`, `TokenCache__*`, `ForwardedHeaders__*`, `Kafka__SecurityProtocol`, `Kafka__Sasl*`, `Kafka__EnableIdempotence`, `LiveUpdates__*`. With it `false`, Analytics cannot reach Event Hubs; set Analytics min and max to 0 until P3, and record that.

P3 — Scale-out settings and scale demonstration (one approval, after Tracks A and B have merged to `main`)
20. Rebuild images from the new `main`, set `enableScaleOutSettings=true`, restore Analytics to min 1 / max 4, and redeploy.
21. Add `infra/scripts/scale-demo.sh`, a bounded, opt-in load script for this environment only. It:
    - takes the short code of one managed demo link as `--managed-code` (Michael creates it once after signing in), and optionally creates one guest link;
    - sends anonymous redirect requests to the managed link (and the guest link, if created) at a fixed concurrency for a fixed duration (defaults: 60 concurrent for 3 minutes; hard caps: 100 concurrent, 5 minutes), with no other endpoints. Managed redirects are anonymous and each one produces a click event, so the run drives both API HTTP scaling and Analytics Kafka-lag scaling;
    - samples `az containerapp replica list` for API, Web, and Analytics every 15 seconds during the run and for 5 minutes after, and the Event Hubs consumer-group lag, printing a timestamped table;
    - after the run, prints the managed link's hit count from the API's authorized analytics endpoint only if a token is supplied through an environment variable; otherwise states that the count must be checked in the dashboard;
    - prints request totals, error rate, and latency percentiles;
    - refuses to run unless `--confirm-environment rg-schwab-url-shortener` is passed.
    Use a load tool available via Homebrew or a container (for example `hey`), and record which.
22. Run it once with Michael's approval and record: API, Web, and Analytics replicas before, at peak, and after scale-in; consumer lag over time; requests, errors, and latency; and that every click was counted once (the dashboard count rises by exactly the number of successful managed redirects). If Michael has not yet created the managed demo link, ask him for it; do not create managed rows directly in the database.

Out of scope:
- A staging or second environment, Front Door, private endpoints for Key Vault/Storage/Event Hubs, Azure Managed Redis, PgBouncer, WAF, multi-region, zone redundancy and PostgreSQL high availability, Azure SignalR Service, PostgreSQL Entra authentication and Event Hubs OAuth (both need application code changes). List each as a documented production step with its approximate added monthly cost.
- Entra app registration changes (Michael does these).
- Any change under `src/`, `tests/`, `Dockerfile`, or Compose files.

Required discovery before edits:
- Read the revised contract in full and your existing `infra/**`, `azure.yaml`, and validation note.
- Check Microsoft documentation, recording URLs, for: PostgreSQL Flexible Server private access (VNet integration) on the Burstable tier and its private DNS zone requirements; Container Apps Azure Files volume mounts and their account-key requirement; Container Apps environment path-based routing status; Container Apps TCP ingress for an internal Redis app; service endpoint support for Key Vault, Storage, and Event Hubs Standard from a Container Apps subnet; `Standard_B1ms` `max_connections`; KEDA Kafka scaler with SASL in Container Apps; Container Apps managed certificates for a custom domain.
- Record the free trial's end date from the portal or billing CLI if available; otherwise note that Michael must check it.

Implementation requirements:
- No secret appears in any committed file, deployment output, log, or chat. Secrets flow only through Key Vault and Container Apps Key Vault references using managed identities.
- Every app sets `AZURE_CLIENT_ID` to its own identity.
- Least privilege per identity: no shared identities, no vault-wide Key Vault roles. In P1 testing, confirm with `az role assignment list` that each identity has exactly the contract's assignments.
- Bicep lints with zero errors and warnings, or each remaining warning is listed with a reason.
- Application images deploy by digest; Redis and Caddy images by pinned digest.
- Every lean choice has a one-line production upgrade note in `infra/README.md`.

Exception / observability requirements:
- All apps log to Log Analytics; record the query that shows each app's "Service started" event and the query that shows replica scale events.
- Set `APPLICATIONINSIGHTS_CONNECTION_STRING` on all apps for later export.

Realtime / no-polling requirements:
- Verify WebSocket upgrades reach Web through the public host, because Blazor's live connection and Track B's push depend on them.

Testing:
- Offline: Bicep build and lint, `azd provision --preview` per phase.
- P1: confirm resources exist, network rules applied, Key Vault secret names present (names only).
- P2: through the public host, `GET /` 200, `GET /health/ready` 200, guest create 201, `/r/<code>` 302, WebSocket upgrade to Web succeeds; migration job exit 0.
- P1: confirm PostgreSQL has no public endpoint (`az postgres flexible-server show` network settings) and resolves to a private IP from inside the environment.
- P2: Redis data survives a replica restart; each alert rule exists and its action group targets the budget email.
- P3: Analytics `/health/ready` 200 against Event Hubs; a click on the managed demo link flows outbox → Event Hubs → Analytics aggregate; the app observes the public host and `https` (verify forwarded headers); the scale demonstration results.
- Record what was not exercised, including live Entra sign-in until Michael adds redirect URIs.

Documentation:
- Update `docs/2026-10-13-1757-track-c-azure-infrastructure-validation.md` with a dated section for this revision: provider registrations, resource inventory, what-if summaries, connection-budget arithmetic, routing choice and evidence, smoke results, scale demonstration table, estimated monthly cost (calculator-based), trial end date, and remaining operator actions.
- Rewrite `infra/README.md` for the single environment: `azd` values, phases, `enableScaleOutSettings`, migration job, scale demo usage, and production upgrade steps.
- Update the root README surgically with a link to `infra/README.md` and the public host.

Completion criteria:
- [ ] One environment, `rg-schwab-url-shortener`, deployed per the contract with no secret committed or logged.
- [ ] Guest flow and WebSocket work through the public host.
- [ ] After Tracks A and B merge: scale-out and live-update settings enabled; Analytics consumes from Event Hubs.
- [ ] Autoscale rules are configured on API, Web, and Analytics; Web is sticky.
- [ ] The scale demonstration ran once and recorded replica counts scaling out and back in.
- [ ] Estimated monthly cost recorded; budget at USD 100 with alerts.
- [ ] Production upgrade path documented for every lean choice.
- [ ] Only `infra/**`, `azure.yaml`, and dated docs changed.

Report back: final branch commit, the public host, smoke and scale-demo results, estimated monthly cost, trial end date, and Michael's remaining actions (redirect URIs, trial upgrade).
