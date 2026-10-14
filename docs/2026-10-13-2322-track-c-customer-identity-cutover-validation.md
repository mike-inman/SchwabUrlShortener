# Track C customer identity cutover validation — 2026-10-13 23:22 CDT

## Scope and source

The 23:20 contract and corrected Entra addendum were copied byte-for-byte from the canonical checkout and committed as `f639716`. The contract's new Identity section governs over older workforce references elsewhere in those historical documents. The deployment targets only subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, resource group `rg-schwab-url-shortener`, and the `prod` azd environment. No database or unrelated resource group was touched.

The customer Web registration is `93f2f816-0a95-41e0-b7df-7ca3c0cce8a0` in Ziplio tenant `6adc36e9-dfe5-469f-8aed-6ef83abcdd19`. The Entra reconciliation script reads Graph only through the separate `$HOME/.azure-ziplio` CLI profile, verifies that tenant, and does not change the default Azure CLI profile. A live `--dry-run` before deployment and another afterward both reported `Redirect URIs: no changes` and `Dry run: no Entra writes`. No Graph PATCH or POST was made.

## Provider-backed preview and deployment

`infra/scripts/deploy-customer-identity.py --what-if` used a secure ARM parameter to pass the local azd `WEB_CLIENT_SECRET` to Key Vault. The final provider preview marked exactly these four resources `Modify`:

| Resource | Reported change |
| --- | --- |
| `ca-shortener-api` | Container environment array with customer identity settings |
| `ca-shortener-web` | Container environment array with customer identity settings and customer API scope |
| `caj-shortener-migrate` | Container environment array with customer identity settings |
| Key Vault `web-client-secret` | Secret `contentType` marker for this customer secret version; secure value hidden |

The remaining reported identity, ingress, and storage fields were `NoEffect`. Azure what-if reports container arrays at array granularity. The script starts from each live definition and changes only the identity environment entries. The same focused script then ran `--apply` successfully. ARM secret metadata subsequently showed `contentType=external-id-web-client-secret-2026-10-13` and `enabled=true`; its value was never read or displayed.

Live ARM GET showed the three resources provisioned successfully. API and migration job use `Identity__Mode=Customer`, `AzureAd__Instance=https://schwaburlshortener.ciamlogin.com/`, the Ziplio tenant ID, and customer API client ID `8911f6b3-365c-4ce5-a0f7-d12b2e4f230a`. Web uses Customer mode, authority `https://schwaburlshortener.ciamlogin.com/6adc36e9-dfe5-469f-8aed-6ef83abcdd19/v2.0`, customer Web client ID, public callback paths, customer API scope, and the Key Vault secret reference.

## Runtime checks

| Request | Observed |
| --- | --- |
| Public `/health/ready` routed to API | HTTP 200 |
| Web replica localhost `/health/ready` with its allowed origin Host | HTTP 200 |
| Public `/account/signin` | HTTP 302 to `schwaburlshortener.ciamlogin.com`, `redirect_uri=https://routeshortener.happysand-837faf44.centralus.azurecontainerapps.io/signin-oidc` |
| Public protected `/dashboard` | Same customer authority and public callback, HTTP 302 |
| Public `/` | HTTP 200 anonymous home page |

The requested literal `/` redirect did not occur. The existing Web Home page is anonymous and shows a sign-in link; sign-in and protected routes make the correct customer redirect. Changing `/` to redirect requires an application behavior change and a new image deployment, outside this settings-only preview. Live completion of the customer sign-in and token exchange still requires an interactive user flow.

The Web managed identity is in the workforce tenant while the customer Web registration is in Ziplio. Microsoft requires the managed identity and app registration to share a tenant for managed-identity federated credentials, so the customer deployment retains the Key Vault client secret and the script declares no federated credential.
