## Track C addendum — `ziplio.io` custom domain, DNS as code
Status: drafted 2026-10-13 23:30 CDT. Applies to Track C (`/Users/michaelinman/Repos/SchwabUrlShortener-infra`, branch `codex/azure-infra`). **Do this after customer-mode sign-in is verified on the current public host.** Follow all Track C standards; copy this addendum into your worktree's `docs/` and commit it first.

Why:
Michael owns `ziplio.io` (registrar and DNS: GoDaddy, nameservers `ns75/ns76.domaincontrol.com`, apex currently parked). He wants no manual configuration. GoDaddy's DNS API is not available to small accounts, so DNS moves to an Azure DNS zone managed in Bicep. The only manual step is a one-time nameserver change at the registrar.

Current GoDaddy records (observed 2026-10-13 23:20 CDT): `A @ Parked`; `NS @ ns75/ns76.domaincontrol.com`; `CNAME www → ziplio.io`; `CNAME _domainconnect → _domainconnect.gd.domaincontrol.com`; `SOA`; `TXT _dmarc "v=DMARC1; p=quarantine; adkim=r; aspf=r; rua=mailto:dmarc_rua@onsecureserver.net;"`.

Decisions:
- Primary host: the apex `ziplio.io` (shortest links: `ziplio.io/abc123`). `www.ziplio.io` redirects permanently to the apex.
- The sign-in page stays on `schwaburlshortener.ciamlogin.com` with Ziplio branding; an External ID custom URL domain needs Front Door and is a documented production step.
- The domain sends no email: publish `TXT @ "v=spf1 -all"` and keep the DMARC record (Michael may later change the `rua` address; preserve it as-is for now).

In scope only:
1. Bicep: Azure DNS zone `ziplio.io` in `rg-schwab-url-shortener`, tagged per the contract, with:
   - `A @` → the Container Apps environment's static inbound IP.
   - `TXT asuid` → the environment's custom domain verification ID (apex validation).
   - `TXT @` → `v=spf1 -all`.
   - `TXT _dmarc` → the exact current value above.
   - `CNAME www` → the Container Apps environment default host used by the public route, plus `TXT asuid.www` for verification, so `www` can be bound and redirected.
   - No `_domainconnect` record.
   Output the zone's four Azure nameservers.
2. Custom domain binding with Container Apps managed certificates for `ziplio.io` and `www.ziplio.io` on the public route. Order: DNS records first, then hostname binding, then certificate. Handle propagation by polling public DNS (bounded, for example up to 60 minutes) before binding; never loop indefinitely.
3. `www` → apex redirect: use the platform's routing redirect if the Container Apps HTTP route configuration supports it (check Microsoft documentation and record the URL); otherwise record it for Track D as a small application redirect (a host check returning 301 to `https://ziplio.io` with path and query preserved), and leave `www` bound in the meantime.
4. Configuration: set `CUSTOM_DOMAIN=ziplio.io` in the `azd` environment so `AllowedHosts`, `PublicLinks__BaseUrl=https://ziplio.io/`, and the Entra redirect URIs follow it, per the contract and the Entra addendum. Generated short links must use `https://ziplio.io/`.
5. Entra: through the Entra script (dry run shown to Michael first), add `https://ziplio.io/signin-oidc` and `https://ziplio.io/signout-callback-oidc` to the customer Web registration `93f2f816-0a95-41e0-b7df-7ca3c0cce8a0`. After sign-in on `https://ziplio.io` is verified, remove the `azurecontainerapps.io` redirect URIs.
6. Registrar step: give Michael the four nameservers and the exact GoDaddy path (**Domain Portfolio → ziplio.io → DNS → Nameservers → Change Nameservers → I'll use my own nameservers**). Michael may ask Claude to do it in his browser. Before the change, verify the Azure zone serves every record above (query the Azure nameservers directly), so nothing breaks at cutover.

Out of scope:
- Email hosting, MX records, DNSSEC (document as a later step), Front Door, and External ID custom URL domains.

Testing:
- Before the nameserver change: `dig @<azure-ns> ziplio.io A`, `TXT`, `_dmarc.ziplio.io TXT`, `www.ziplio.io CNAME` all return the intended values.
- After propagation: `dig ziplio.io NS` returns the Azure nameservers; `https://ziplio.io/` returns 200 with a valid certificate; `https://www.ziplio.io/x?y=1` redirects to `https://ziplio.io/x?y=1`; guest create returns a `https://ziplio.io/<code>` link that redirects correctly; customer sign-in and sign-out complete on `https://ziplio.io`; the WebSocket upgrade succeeds on the custom domain.
- Record everything in the Track C validation note, add a `docs/runbooks/` entry for DNS and certificate renewal (managed certificates renew automatically; document how to check), and update `infra/README.md`.

Completion criteria:
- [ ] `ziplio.io` DNS is fully defined in Bicep, with no manual records.
- [ ] `https://ziplio.io` serves the app with a managed certificate; `www` redirects to the apex.
- [ ] Short links, sign-in, sign-out, and live updates work on `ziplio.io`.
- [ ] Unused `azurecontainerapps.io` redirect URIs removed after verification.
- [ ] SPF and DMARC published for the non-mail domain.
