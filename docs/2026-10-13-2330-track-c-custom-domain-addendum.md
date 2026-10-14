## Track C addendum — `ziplio.io` custom domain (DNS stays at GoDaddy)
Status: drafted 2026-10-13 23:30 CDT. **Revised 23:26 CDT: Michael does not want DNS managed in Azure. DNS stays at GoDaddy; no Azure DNS zone and no nameserver change.** Applies to Track C (`/Users/michaelinman/Repos/SchwabUrlShortener-infra`, branch `codex/azure-infra`). **Do this after customer-mode sign-in is verified on the current public host.** Follow all Track C standards; copy this addendum into your worktree's `docs/` and commit it first.

Why:
Michael owns `ziplio.io` (registrar and DNS: GoDaddy, nameservers `ns75/ns76.domaincontrol.com`, apex currently parked) and wants DNS to remain at GoDaddy, not in Azure. GoDaddy's DNS API is not available to this account, so the few required records are added in GoDaddy's DNS page by Claude through Michael's browser, with Michael's approval before saving. Track C defines exactly which records are needed and verifies them; it does not edit GoDaddy.

Current GoDaddy records (observed 2026-10-13 23:20 CDT): `A @ Parked`; `NS @ ns75/ns76.domaincontrol.com`; `CNAME www → ziplio.io`; `CNAME _domainconnect → _domainconnect.gd.domaincontrol.com`; `SOA`; `TXT _dmarc "v=DMARC1; p=quarantine; adkim=r; aspf=r; rua=mailto:dmarc_rua@onsecureserver.net;"`.

Decisions:
- DNS provider: GoDaddy, unchanged nameservers. No Azure DNS zone.
- Primary host: the apex `ziplio.io` (shortest links: `ziplio.io/abc123`). `www.ziplio.io` redirects permanently to the apex.
- The sign-in page stays on `schwaburlshortener.ciamlogin.com` with Ziplio branding; an External ID custom URL domain needs Front Door and is a documented production step.
- Change only what is needed at GoDaddy: replace the parked apex `A` record, add the verification `TXT` records, add `TXT @ "v=spf1 -all"` (the domain sends no email), keep `_dmarc`, `_domainconnect`, and the existing `www` record unless Container Apps validation requires a different `www` record.

In scope only:
1. Determine the exact GoDaddy records from the Container Apps environment and Microsoft documentation (record the URL): the apex `A` value (the environment's static inbound IP), the `TXT asuid` value (the custom domain verification ID), and what `www` needs (`TXT asuid.www` plus either the existing `CNAME www → ziplio.io` if apex-IP validation accepts it, or a `CNAME www` to the app's FQDN). Produce a table: type, name, value, TTL 600, and whether it adds, replaces, or leaves an existing record. Give it to Michael; Claude applies it in GoDaddy after his approval.
2. After Michael confirms the records are saved, poll public DNS (bounded, up to 60 minutes) until each record resolves to the intended value, then bind `ziplio.io` and `www.ziplio.io` with Container Apps managed certificates on the public route, in Bicep, so the binding is code even though the DNS records are not.
3. `www` → apex redirect: use the platform's routing redirect if the Container Apps HTTP route configuration supports it (check Microsoft documentation and record the URL); otherwise record it for Track D as a small application redirect (301 to `https://ziplio.io` with path and query preserved) and leave `www` bound in the meantime.
4. Configuration: set `CUSTOM_DOMAIN=ziplio.io` in the `azd` environment so `AllowedHosts`, `PublicLinks__BaseUrl=https://ziplio.io/`, and the Entra redirect URIs follow it, per the contract and the Entra addendum. Generated short links must use `https://ziplio.io/`.
5. Entra: through the Entra script (dry run shown to Michael first), add `https://ziplio.io/signin-oidc` and `https://ziplio.io/signout-callback-oidc` to the customer Web registration `93f2f816-0a95-41e0-b7df-7ca3c0cce8a0`. After sign-in on `https://ziplio.io` is verified, remove the `azurecontainerapps.io` redirect URIs.
6. Document in `infra/README.md` and `docs/runbooks/` that `ziplio.io` DNS lives at GoDaddy, list the required records and why each exists, and note that the verification `TXT` records must stay in place for certificate renewal.

Out of scope:
- Moving DNS or nameservers, editing GoDaddy directly, email hosting, MX records, DNSSEC, Front Door, and External ID custom URL domains.

Testing:
- After the GoDaddy records are saved: `dig ziplio.io A`, `dig asuid.ziplio.io TXT`, `dig asuid.www.ziplio.io TXT`, `dig ziplio.io TXT` (SPF), `dig _dmarc.ziplio.io TXT` return the intended values from public resolvers.
- `https://ziplio.io/` returns 200 with a valid certificate; `https://www.ziplio.io/x?y=1` redirects to `https://ziplio.io/x?y=1`; guest create returns a `https://ziplio.io/<code>` link that redirects correctly; customer sign-in and sign-out complete on `https://ziplio.io`; the WebSocket upgrade succeeds on the custom domain.
- Record everything in the Track C validation note and update `infra/README.md`.

Completion criteria:
- [ ] The GoDaddy record table is documented and the records resolve publicly; DNS remains at GoDaddy.
- [ ] `https://ziplio.io` serves the app with a managed certificate; `www` redirects to the apex.
- [ ] Short links, sign-in, sign-out, and live updates work on `ziplio.io`.
- [ ] Unused `azurecontainerapps.io` redirect URIs removed after verification.
- [ ] SPF and DMARC published for the non-mail domain.
