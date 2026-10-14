# Ziplio custom-domain runbook — 2026-10-13 23:42 CDT

DNS is hosted at GoDaddy (`ns75.domaincontrol.com` and `ns76.domaincontrol.com`). Do not create an Azure DNS zone or change nameservers. The table below was checked against GoDaddy's `ns75` and public resolver `8.8.8.8` on 2026-10-13; TTLs are observed GoDaddy values. The two verification TXT values match the Container Apps environment's verification ID and are intentionally omitted from this document.

| Type | GoDaddy name | Value | Observed TTL | Status and purpose |
| --- | --- | --- | --- | --- |
| A | `@` | `20.12.161.197` | 600 | Replaced parked apex; Container Apps environment inbound IP |
| TXT | `asuid` | Environment verification ID | 3600 | Added; apex ownership and certificate renewal |
| TXT | `asuid.www` | Environment verification ID | 3600 | Added; `www` ownership and certificate renewal |
| TXT | `@` | `v=spf1 -all` | 3600 | Added; declares no domain email sending |
| CNAME | `www` | `ziplio.io` | 3600 | Unchanged; `www` resolves through apex |
| TXT | `_dmarc` | `v=DMARC1; p=quarantine; adkim=r; aspf=r; rua=mailto:dmarc_rua@onsecureserver.net;` | 3600 | Unchanged; mail-domain policy |

Keep both `asuid` TXT records in place for managed certificate renewal. Keep the existing `_domainconnect` and NS records at GoDaddy. Recheck values with `dig @ns75.domaincontrol.com` and `dig @8.8.8.8` before renewal troubleshooting. [Microsoft's route custom-domain guidance](https://learn.microsoft.com/en-us/azure/container-apps/rule-based-routing-custom-domain) describes route ownership records and `Auto` certificate binding.

The resource group is `rg-schwab-url-shortener`, environment `cae-shortener`, and HTTP route `routeshortener`. [`p2-custom-domain.bicep`](../../infra/p2-custom-domain.bicep) declares both route host bindings and their managed certificates. Preview with `az deployment group what-if -g rg-schwab-url-shortener --template-file infra/p2-custom-domain.bicep --no-pretty-print`; check that only the route and two certificate resources change before deployment. The route's `Auto` binding can serve HTTP while certificates are pending; validate HTTPS only after both show succeeded. Avoid using `curl -k` for final TLS validation. Use HTTP validation for the managed certificates; the route ownership `asuid` TXT values are separate from the managed certificate TXT validation tokens. Managed certificates are immutable after creation.

The existing `www → ziplio.io` CNAME resolves to the environment IP but is an intermediate CNAME. [Microsoft's managed certificate requirements](https://learn.microsoft.com/en-us/azure/container-apps/custom-domains-managed-certificates) say subdomain CNAMEs should point directly to the generated application host; intermediate CNAMEs can block issuance and renewal. If `www` HTTP validation fails, stop and review the DNS requirement before changing GoDaddy. The route-specific guidance also documents an A record for a route subdomain. Do not silently replace the user-owned `www` record.

The route configuration supports path matching and prefix rewriting, but no host redirect action in its [resource schema](https://learn.microsoft.com/en-us/azure/templates/microsoft.app/2024-10-02-preview/managedenvironments/httprouteconfigs). Keep `www.ziplio.io` bound. Track D must add a 301 from `www` to `https://ziplio.io`, preserving path and query, and validate it end to end. Do not remove `www` DNS or its certificate while that work is pending.

The customer Web registration is reconciled only through the separate Ziplio CLI profile. Run `./infra/scripts/configure-entra.sh --dry-run` and review the two apex callback additions before `--apply`. The current public ACA callback URIs remain registered until custom-domain sign-in and sign-out succeed. Never print client secrets or tokens. The earlier [Entra runbook](2026-10-13-2307-entra-registration-and-domain.md) describes the historical workforce setup and is superseded for customer mode by this runbook and the 23:20 contract Identity section.
