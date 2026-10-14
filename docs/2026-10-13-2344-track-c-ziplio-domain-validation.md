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

## 2026-10-14 08:00 CDT — HTTPS certificate recovery

Purpose: carry out Michael's approved, limited recovery sequence for the two failed Ziplio managed certificates, then verify TLS and the guest-link path. Scope is `cae-shortener` certificates `mc-ziplio-apex` and `mc-ziplio-www`, the existing resource-group delete lock, and the already-prepared API/Web host settings. No DNS, secret, identity, or unrelated Azure resource changes are authorized.

At 12:58 UTC the default Azure CLI account was `social@mediaraq.com` in subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`. Both named certificates showed `Failed`, `TXT`, and `Operation timed out.` Neither `_dnsauth.ziplio.io` nor `_dnsauth.www.ziplio.io` returned a TXT value. The `lock-shortener-cannot-delete` resource-group lock showed `CanNotDelete`.

The lock removal started at **2026-10-14 12:58:37 UTC** and completed at **12:58:40 UTC**. Only `mc-ziplio-apex` was then deleted (completed **12:58:45 UTC**) and `mc-ziplio-www` was deleted (completed **12:58:50 UTC**). The guarded command's exit trap attempted lock restoration at **12:58:50 UTC**; `az lock show` immediately confirmed the same name and `CanNotDelete` level. A later certificate list was empty and the lock still showed `CanNotDelete`. No other certificate was deleted.

[Microsoft's Container Apps managed-certificate guide](https://learn.microsoft.com/en-us/azure/container-apps/custom-domains-managed-certificates) states that an apex domain with an A record uses **HTTP** validation; its table lists **CNAME** validation for a subdomain with a CNAME record. The [managed-certificate resource API](https://learn.microsoft.com/en-us/rest/api/resource-manager/containerapps/managed-certificates/create-or-update?view=rest-resource-manager-containerapps-2026-01-01) permits `HTTP`, `CNAME`, and `TXT`. Michael specified HTTP for both recreated certificates if HTTP is supported. The Bicep uses HTTP for both, including `www`; actual `www` issuance must establish whether this works with the current CNAME.

`infra/p2-custom-domain.bicep` now reuses exactly the two deleted names with `domainControlValidation: 'HTTP'`. Bicep compilation passed. The provider-backed what-if for deployment `shortener-ziplio-http-20261014` showed only: Create `mc-ziplio-apex`; Create `mc-ziplio-www`; Modify `routeshortener` because the provider normalizes `properties.rules`. It showed no Deletes. The existing route already had both custom domains with `Auto` binding and `Succeeded` state. The focused deployment was started with the group lock in place.

Both certificates were polled while issuance ran. Each remained `Pending` without an error until both showed `Succeeded` at the 2026-10-14 13:05:15 UTC check. Deployment `shortener-ziplio-http-20261014` completed `Succeeded` at 13:05:17 UTC. The route then showed both `Auto` bindings with their respective managed-certificate IDs. The lock was checked again and remained `CanNotDelete`. Standard TLS verification (`curl`, no insecure option) passed for both `https://ziplio.io/` and `https://www.ziplio.io/` with `ssl_verify_result=0`. Both initially returned HTTP 400 `Bad Request - Invalid Hostname`; the original route host returned 200. After the two focused app-setting deployments completed and propagated, both custom hosts returned HTTP 200 with valid TLS.

The app-settings script was narrowed to two explicit stages so `AllowedHosts` could be deployed before `PublicLinks__BaseUrl`. The `allowed-hosts` provider what-if showed only API and Web container-array modifications and no Creates or Deletes. Deployment `shortener-domain-allowed-hosts-20261014` succeeded at 13:06:37 UTC. Live API and Web configuration showed `ziplio.io` and `www.ziplio.io` in `AllowedHosts` while their previous ACA `PublicLinks__BaseUrl` values were still intact. The `public-links` provider what-if again showed only API and Web container-array modifications and no Creates or Deletes. Deployment `shortener-domain-public-links-20261014` succeeded at 13:08:10 UTC. Live API and Web configuration then showed `PublicLinks__BaseUrl=https://ziplio.io/`. The lock remained `CanNotDelete` after both deployments.

Anonymous live check through the apex created guest code `zl334e33cd` with HTTP 201. The API response's `shortUrl` was `/r/zl334e33cd`. `https://ziplio.io/r/zl334e33cd` returned HTTP 302 to `https://example.com/`; `https://ziplio.io/zl334e33cd` returned HTTP 404. `src/Shortener.Web/Services/ShortenerApiClient.cs` also builds public links with `r/` before the code. Therefore TLS and the current guest redirect path work, but the requested root-level `https://ziplio.io/<code>` link format is **not met**. Changing the application's URL scheme and route is outside Michael's exact approved recovery sequence. No route or application code change was made to conceal this gap. Both custom hosts currently serve HTTP 200; `www` is not yet redirected to the apex, as separately recorded above.
