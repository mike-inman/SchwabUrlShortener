# Entra registration and custom-domain runbook — 2026-10-13 23:07 CDT

## Redirect reconciliation

The committed [`web-registration.json`](../../infra/entra/web-registration.json) declares the SchwabUrlShortener Web registration and callback paths. `PUBLIC_HOST` in the `prod` azd environment is the active public hostname. The `postprovision` hook invokes [`configure-entra.sh`](../../infra/scripts/configure-entra.sh) with `--apply`; the script reads current Web redirect URIs through Microsoft Graph and prints the URI-only diff before writing. It removes only stale `.azurecontainerapps.io` callback URIs or callback URIs on `PREVIOUS_CUSTOM_DOMAIN`. It preserves every unrecognized URI and warns. No app secret or Graph token is shown or stored.

Before provisioning, run `./infra/scripts/configure-entra.sh --dry-run` and review any `ADD` or `REMOVE`. Today, the expected result is `Redirect URIs: no changes`. To inspect a proposed domain without changing azd or Entra, run `./infra/scripts/configure-entra.sh --dry-run --simulate-custom-domain links.example.org` with a harmless example hostname. The actual domain must be one you control.

The signed-in `az` identity must own Web registration `d4cd63a0-5583-41ad-a640-803ea76bb3a8`. For Track F, add the CI deployment identity as an owner and grant Microsoft Graph `Application.ReadWrite.OwnedBy`; until that is done, the hook reports that CI reconciliation is skipped. Do not grant broad application-write permissions merely to make the hook run.

## Federated credential and Web secret

The optional `--include-federation` flag previews or creates the single named Web credential for `id-shortener-web`. Its issuer, principal-ID subject, and audience are checked against the declared configuration. A conflicting credential stops the script for review. The default hook keeps federation off while Web runs with its existing Key Vault client secret. After a separately reviewed federated-credential diff, validate live sign-in with the managed identity configuration before removing the Web secret reference. Rotate or retire that secret gradually under the [secret rotation runbook](2026-10-13-1935-secret-rotation.md); never print, delete, or overwrite a local password or API key as part of redirect reconciliation.

## Custom domain prerequisite and cutover

A domain is not provisioned by the current P1-only `azd` entry point. Before setting `CUSTOM_DOMAIN`, implement and preview the P2 route binding, managed certificate creation, `AllowedHosts`, `PublicLinks__BaseUrl`, and `PUBLIC_HOST` cutover. Azure requires ownership DNS records; with a rule-based route's `Auto` binding, HTTPS becomes available only after the managed certificate is created. See [Microsoft's rule-based route domain guidance](https://learn.microsoft.com/en-us/azure/container-apps/rule-based-routing-custom-domain).

Once the domain is bound, its HTTPS route and Web sign-in callback have been validated, set `CUSTOM_DOMAIN` and use the Entra script's dry run to review the two callback additions. Keep the existing ACA callbacks until the custom domain is the verified active `PUBLIC_HOST`. Then set `PREVIOUS_CUSTOM_DOMAIN` if replacing an older custom domain, change `PUBLIC_HOST` to the new hostname, preview again, and reconcile. This removes only previously owned callbacks and preserves unrelated registration entries.
