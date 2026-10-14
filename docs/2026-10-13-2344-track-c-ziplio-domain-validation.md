# Track C Ziplio custom-domain validation — 2026-10-13 23:44 CDT

## Ownership and DNS

DNS remains at GoDaddy (`ns75.domaincontrol.com`, `ns76.domaincontrol.com`). On 2026-10-13, both GoDaddy `ns75` and `8.8.8.8` returned the intended records. The apex IP matches Container Apps environment `cae-shortener`; both `asuid` TXT values match its verification ID. Verification IDs are not copied into this note.

| Type | GoDaddy name | Value | Observed TTL | State |
| --- | --- | --- | --- | --- |
| A | `@` | `20.12.161.197` | 600 | Replaced parked apex |
| TXT | `asuid` | Environment verification ID | 3600 | Added; matches Azure |
| TXT | `asuid.www` | Environment verification ID | 3600 | Added; matches Azure |
| TXT | `@` | `v=spf1 -all` | 3600 | Added |
| CNAME | `www` | `ziplio.io` | 3600 | Unchanged |
| TXT | `_dmarc` | `v=DMARC1; p=quarantine; adkim=r; aspf=r; rua=mailto:dmarc_rua@onsecureserver.net;` | 3600 | Unchanged |

The `asuid` records must remain for managed certificate renewal. No GoDaddy record or Azure DNS resource was changed by Track C.

## Azure preview and application

The first focused provider what-if for `infra/p2-custom-domain.bicep` showed only: `routeshortener` route modification to add two custom domains, `mc-ziplio-apex` creation, and `mc-ziplio-www` creation. Azure reported its route `rules` array as modified due to default normalization; the Bicep keeps the existing API/Web matches and targets. The route succeeded with both `Auto` bindings. The two certificates remained Pending because `domainControlValidation=TXT` generated separate validation tokens that did not match the published `asuid` ownership TXT records. The pending deployment was canceled.

A second provider what-if proposed changing only the two certificate validation methods to HTTP plus the route rules normalization. Azure rejected deployment with `ManagedCertNoUpdate`: certificate resources are immutable. A third what-if proposed two new HTTP validated certificates (`mc-ziplio-apex-http`, `mc-ziplio-www-http`) plus the route normalization. Azure rejected creation with `ExistingManagedCertificateOperationInProgress`, naming the two original pending resources. The route shows no bound certificate IDs yet. An attempted deletion of only `mc-ziplio-apex` returned `ScopeLocked` from the resource group's `CanNotDelete` lock. No lock change or certificate deletion has occurred. Certificate issuance and HTTPS acceptance remain blocked pending an explicit decision on temporary lock removal and cleanup.

The local `prod` azd environment has `CUSTOM_DOMAIN=ziplio.io`. A separate provider what-if for focused API/Web host settings showed only modifications to `ca-shortener-api` and `ca-shortener-web`, each at container-array granularity. Those settings have not been deployed. They will retain the ACA host and add apex/`www` to `AllowedHosts`, then set `PublicLinks__BaseUrl=https://ziplio.io/` after HTTPS is ready.

The customer Entra script's `--dry-run` showed only two additions, `https://ziplio.io/signin-oidc` and `https://ziplio.io/signout-callback-oidc`, with no removals or writes. After that preview was shown, `--apply` added exactly those URIs and verified the registration. It used the separate Ziplio CLI profile. The ACA callbacks remain registered until custom-domain sign-in and sign-out are verified.

## Redirect and remaining checks

[Microsoft's route custom-domain documentation](https://learn.microsoft.com/en-us/azure/container-apps/rule-based-routing-custom-domain) supports `Auto` managed certificate binding. The [HTTP route schema](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/2024-10-02-preview/managedenvironments/httprouteconfigs) exposes only prefix rewriting as a route action, so this route cannot implement a host-level 301. Both hostnames will remain bound. Per the addendum, Track D must add an application redirect from `www.ziplio.io` to `https://ziplio.io` that preserves path and query, then verify it.

After certificates succeed: verify TLS on both hosts, deploy the focused app settings, and test apex links, customer sign-in/sign-out, WebSocket upgrade, and the later `www` redirect. Do not remove the ACA callbacks until the custom-domain auth flow completes.
