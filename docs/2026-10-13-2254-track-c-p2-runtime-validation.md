# Track C P2 runtime validation — 2026-10-13 22:54 CDT

## Purpose and scope

Record the approved `prod` P2 deployment and its live checks in subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, resource group `rg-schwab-url-shortener`. This record covers application/runtime validation and removal of temporary operator IP rules. It does not approve P3, modify application data directly, or exercise live Entra sign-in.

## Tasks and acceptance evidence

| Task | Result |
| --- | --- |
| PostgreSQL `citext` allowlist | Provider what-if predicted one configuration change. `shortener-p2-citext-approved` succeeded; live `azure.extensions` is exactly `citext` with source `user-override`. |
| Migration | Execution `caj-shortener-migrate-50yfgcm` finished `Succeeded`, container exit code `0` at 2026-10-14 03:36:10 UTC. |
| Key Vault references and app health | API, Web, Analytics, and Redis containers started using their existing Key Vault references and are healthy. No Key Vault network rule was changed to make them work. API `ca-shortener-api--0000002`, Web `ca-shortener-web--0000002`, and Analytics `ca-shortener-analytics--2pyspwf` were healthy and running at closeout. |
| Public HTTP sequence | `https://routeshortener.happysand-837faf44.centralus.azurecontainerapps.io/` returned `200`; `/health/ready` returned `200`; anonymous `POST /api/urls/guest` returned `201` and code `FZnceYHI`; `/r/FZnceYHI` returned `302` to `https://example.com/`. The test-created guest link expires by application policy. |
| WebSocket | `POST /_blazor/negotiate?negotiateVersion=1` returned `200` with WebSockets offered; `GET /_blazor` with its negotiation token and upgrade headers returned `HTTP/1.1 101 Switching Protocols`. The negotiation token was not recorded. |
| Redis persistence | Set nonsecret key `p2:persistence:20261013` to `smoke-ok` on the original Redis replica. After `az containerapp revision restart`, the replacement replica `ca-shortener-redis--kk5pdtt-f4dd95fc9-bhdst` returned `smoke-ok`; it is now the only ready replica. Redis uses the Azure Files mounted AOF volume. |
| Analytics readiness | Inside the Analytics container, HTTP `GET /health/ready` returned `HTTP/1.1 200 OK`. `AnalyticsReadiness.CheckAsync` checks PostgreSQL `SELECT 1` and Kafka metadata for the configured Event Hubs topic `managed-clicks-v2`. |
| Public host and protocol | The Web sign-in challenge returned `302` with `redirect_uri=https://routeshortener.happysand-837faf44.centralus.azurecontainerapps.io/signin-oidc`. This confirms Web sees the public host and HTTPS. |
| Operator IP cleanup | Provider-backed `what-if` showed four modifications only: removal of `ipRules` on Key Vault, Event Hubs, and both storage accounts. Deployment `shortener-p2-remove-operator-ip` succeeded. All four live `ipRules` arrays are empty; their `defaultAction` remains `Deny`. Key Vault bypass remains `AzureServices`. |

## Runtime repairs captured in IaC

The initial API and Web revisions started but failed health probes because the ingress probes used the pod IP as `Host`, which their `AllowedHosts` rejected. Both liveness and readiness probes now send each app's own origin hostname. The Container Apps API and Web resource PATCH preview contained only these four probe-header additions; both patched revisions became healthy. `infra/p2-apps.bicep` carries the same probe settings.

The first healthy Web revision generated an `http://` OpenID Connect callback. The Web container's observed ingress peers were in `100.100.0.0/17`, the range Microsoft reserves for Azure Container Apps, while the trusted proxy setting contained only `10.60.0.0/23`. A narrowly scoped API/Web PATCH added `100.100.0.0/17` to `ForwardedHeaders__KnownNetworks`; `infra/p2-apps.bicep` carries the same setting. The new revisions generated an HTTPS callback. References: [Azure Container Apps custom VNet reserved ranges](https://learn.microsoft.com/en-us/azure/container-apps/custom-virtual-networks), [ASP.NET Core forwarded-header proxy trust](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer).

## Operator follow-up and remaining acceptance

Add these **Web platform redirect URIs** to the SchwabUrlShortener Web app registration (`d4cd63a0-5583-41ad-a640-803ea76bb3a8`):

- `https://routeshortener.happysand-837faf44.centralus.azurecontainerapps.io/signin-oidc`
- `https://routeshortener.happysand-837faf44.centralus.azurecontainerapps.io/signout-callback-oidc`

The sign-in URI was observed in the live challenge. The sign-out URI is the framework's default `SignedOutCallbackPath`, supported by [Microsoft's OIDC guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication). Live Entra sign-in/sign-out remain untested until the registration is updated. P3 telemetry, managed-link click flow, and scale demonstration remain outside this P2 record.
