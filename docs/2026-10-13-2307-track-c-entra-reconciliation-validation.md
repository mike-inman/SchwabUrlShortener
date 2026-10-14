# Track C Entra reconciliation validation — 2026-10-13 23:07 CDT

## Purpose and scope

Implement the [23:05 addendum](2026-10-13-2305-track-c-entra-as-code-addendum.md) without changing the live Entra registration before Michael sees a dry run. The addendum was copied byte for byte from the canonical checkout and committed as `9bc53ae`. The `prod` azd environment now holds the nonsecret `PUBLIC_HOST`; its `OPERATOR_PUBLIC_IP` was cleared so a future P1 provision does not reopen the removed operator IP rules. No secret values were read or displayed.

## Tasks and acceptance results

- Graph GET returned one Web registration with client ID `d4cd63a0-5583-41ad-a640-803ea76bb3a8` and exactly the local sign-in URI plus both current public-host callbacks. The existing federated-credential collection was empty.
- The first `./infra/scripts/configure-entra.sh --dry-run` printed `Redirect URIs: no changes` and `Dry run: no Entra writes` against the live registration.
- A dry run with simulated `links.example.org` printed exactly two additions, `/signin-oidc` and `/signout-callback-oidc`. It made no write.
- A separate `--dry-run --include-federation` printed one proposed named credential addition for `id-shortener-web`; it made no write. This opt-in reconciler follows the previously confirmed Microsoft documentation for managed-identity assertions and multitenant Web apps sharing the identity's home tenant.
- Four offline reconciliation tests passed: current-state no-op, custom-domain additions, removal of only owned stale callbacks while preserving unknown URIs, and URI display redaction.
- `azure.yaml` now has a `postprovision` hook for redirect reconciliation. No `azd provision` or Graph PATCH/POST was run in this work.

## Remaining work

The credential proposal still needs Michael's review before its Graph write. Keep the Key Vault Web client secret in use until credential-backed live sign-in is verified. Custom-domain Bicep, DNS/certificate binding, and application host cutover are not implemented by the current P1-only `azd` entry point; the [runbook](runbooks/2026-10-13-2307-entra-registration-and-domain.md) states the prerequisite steps. Live Entra sign-in and P3 were not exercised.
