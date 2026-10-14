# Track C P2 apps deployment plan — 2026-10-13 21:35 CDT

## Authority and source

Michael approved P2 conditionally on a scoped what-if in `rg-schwab-url-shortener`. The application source is `origin/main` commit `912ac3b2090c7267b6aa8a295877b643b3eab729`; Tracks A and B are merged there. P1 foundation is verified and locked. The retired staging resource group must not be recreated.

Build API, Web, and Analytics with `infra/scripts/build-images.sh` and `IMAGE_SOURCE_REF=912ac3b2090c7267b6aa8a295877b643b3eab729`, using ACR `--target` stages. Deploy only digest references. `enableScaleOutSettings=true`, Analytics min 1/max 4, and Kafka producer idempotence true are included in P2. Track D telemetry and the scale demonstration remain P3.

## Deployment boundary

`infra/p2-apps.bicep` references P1 VNet, subnet, Log Analytics, App Insights, ACR, storage, Key Vault, and the five individual identities as `existing`. Its new resources are exactly one Container Apps environment, its Azure Files storage binding, the native HTTP route, Redis, API, Web, Analytics, and a manual migration job. No P1 secret, firewall, database, or group resource is redeployed. It uses `routeshortener.<environment defaultDomain>` as the public host, with internal ingress on the app FQDNs and public exposure only through the route. Native route configuration is GA in the Azure CLI. HTTP ingress supports WebSocket, and Web runs single revision with sticky affinity. Validate WebSocket live before calling this complete.

## Ordered gate

1. Verify subscription, group, branch, clean status, image digest refs, and source commit.
2. Run a P2 what-if with resource IDs only. Proceed only if all changes are new resources listed above in the one approved group; otherwise stop for review.
3. Deploy that exact template and parameters. Verify each Key Vault reference resolves under the existing vault network rules first. If one fails, stop and report without changing a network rule.
4. Start the manual migration job once and require exit 0. Check app health and the public route: `/` 200, `/health/ready` 200, guest create 201, redirect 302, WebSocket upgrade to Web. Check Redis persistence across a replica restart, Analytics readiness against Event Hubs, and observed public host and HTTPS.
5. Preview removal of operator IP rules on Key Vault, both storage accounts, and Event Hubs. Record the preview; remove only the IP rules and confirm subnet/service endpoint access remains.
6. Record public host and exact Web app registration redirect URIs (`https://<host>/signin-oidc` and `https://<host>/signout-callback-oidc`). Live sign-in requires that operator portal step and is separate from guest smoke tests.

## Evidence and docs

- [Container Apps routing](https://learn.microsoft.com/azure/container-apps/rule-based-routing)
- [HTTP ingress, WebSocket, and internal route targets](https://learn.microsoft.com/azure/container-apps/ingress-overview)
- [Sticky sessions](https://learn.microsoft.com/azure/container-apps/sticky-sessions)
- [Azure Files mounts](https://learn.microsoft.com/azure/container-apps/storage-mounts-azure-files)
- [Kafka scaler authentication](https://keda.sh/docs/2.21/scalers/apache-kafka/)

Deployment evidence goes in the dated Track C validation note. Do not print, log, or commit secret values. Do not edit application databases or unrelated services.
