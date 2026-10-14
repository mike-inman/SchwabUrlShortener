## Track C addendum — Entra app registration configuration as code
Status: drafted 2026-10-13 23:05 CDT. **Corrected 23:20 CDT: target the customer Web registration `93f2f816-0a95-41e0-b7df-7ca3c0cce8a0` in External ID tenant `6adc36e9-dfe5-469f-8aed-6ef83abcdd19`, not the workforce registration; see the contract's Identity section.** All Graph calls use the separate CLI profile `AZURE_CONFIG_DIR=$HOME/.azure-ziplio`. Applies to Track C (`/Users/michaelinman/Repos/SchwabUrlShortener-infra`, branch `codex/azure-infra`). Do this before P3.

Follow all standards in the Track C handoff. Copy this addendum into your worktree's `docs/` and commit it first.

Why:
Michael does not want to configure Entra by hand in the portal. Redirect URIs (and, if supported, the Web federated credential from Well-Architected item S4) must be applied by an idempotent, reviewable script that runs with every provision, so the registration always matches the deployed hosts.

Current state (set by Claude at 23:02 CDT with Microsoft Graph, after `az ad app update --web-redirect-uris` failed with "One or more properties contains invalid values", a known CLI issue):
- SchwabUrlShortener Web (`d4cd63a0-5583-41ad-a640-803ea76bb3a8`) redirect URIs:
  - `https://localhost:7130/signin-oidc`
  - `https://routeshortener.happysand-837faf44.centralus.azurecontainerapps.io/signin-oidc`
  - `https://routeshortener.happysand-837faf44.centralus.azurecontainerapps.io/signout-callback-oidc`

In scope only:
1. `infra/entra/web-registration.json`: the declared, committed configuration for the SchwabUrlShortener Web registration: the application client ID, the local development redirect URIs (`https://localhost:7130/signin-oidc`), and the callback paths (`/signin-oidc`, `/signout-callback-oidc`). No secrets.
2. `infra/scripts/configure-entra.sh`, idempotent:
   - Computes the desired redirect URI set: the local development URIs, plus both callback paths for the current public host (from the `azd` environment), plus both callback paths for `CUSTOM_DOMAIN` when set.
   - Reads the current `web.redirectUris` with Microsoft Graph (`az rest`), shows the diff (adds and removes), and applies it with a Graph `PATCH` to `/v1.0/applications/{objectId}` that sets `web.redirectUris` to exactly the desired set.
   - Removes only URIs this deployment owns: hosts ending in `.azurecontainerapps.io` that are not the current public host, and a previous custom domain recorded in the `azd` environment. Never removes anything else; if an unrecognized URI exists, keep it and print a warning.
   - Supports `--dry-run`, which prints the diff and changes nothing.
   - Prints only URIs and the registration ID; never a secret or token.
3. Federated identity credential (Well-Architected S4), if Track C's earlier documentation check confirmed support for this multitenant web registration: the same script ensures exactly one federated credential on the Web registration trusting `id-shortener-web` (issuer `https://login.microsoftonline.com/eea269ab-1571-48a5-a18c-824ba5859cd5/v2.0`, subject the identity's principal ID, audience `api://AzureADTokenExchange`), via Graph, idempotently. Gate Web's use of it behind configuration, keep `web-client-secret` as the fallback until live sign-in with the credential is verified, then record whether the secret can be retired.
4. Wire the script as an `azd` `postprovision` hook, so every `azd provision` reconciles the registration. It must run as an identity that owns the registration (today: `social@mediaraq.com`). Record for Track F that its deployment identity must be added as an owner of the Web registration (with Microsoft Graph `Application.ReadWrite.OwnedBy`) before CI can run this hook, or that CI skips the hook with a clear message.
5. Custom domain readiness: document in `infra/README.md` that setting `CUSTOM_DOMAIN` plus `azd provision` binds the domain (managed certificate), updates `AllowedHosts` and `PublicLinks__BaseUrl`, and reconciles redirect URIs. Once sign-in on the custom domain is verified, the `azurecontainerapps.io` URIs are removed by setting the public host to the custom domain, so only hosts in use stay registered.

Out of scope:
- Changing the API registration, scopes, permissions, consent, or sign-in audience.
- Running the federated-credential or redirect change against Entra without first showing Michael the `--dry-run` diff.

Testing:
- `--dry-run` against the current registration shows no change (it already matches).
- A dry run with a simulated `CUSTOM_DOMAIN` shows exactly two adds.
- Re-running the script is a no-op.
- If the federated credential is added: Web signs in on the public host using it, with the secret reference removed from the running revision; record the evidence, then restore the configuration Michael approves.

Documentation:
- Record results in the Track C validation note and in `docs/runbooks/` (how to add a domain, how redirect URIs are reconciled, how to rotate or retire the Web secret).
- Update the Well-Architected review's status for S4.
