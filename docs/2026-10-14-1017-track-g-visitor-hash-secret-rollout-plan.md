# Track G visitor-hash secret rollout plan — 2026-10-14 10:17 CDT

## Purpose and scope

Prepare the production API's private visitor-hash secret before Track G can merge, then deploy Track G images in migration-first order after it reaches `main`. Target only subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, resource group `rg-schwab-url-shortener`, Key Vault `kv-shortener-f2xpl6`, identity `id-shortener-api`, and Container App `ca-shortener-api` for the first two steps. Preserve all existing credentials, resources, and the `CanNotDelete` lock. Do not deploy Track G images before its code is on `main`.

## Prepared source and previews

- `infra/p2-apps.bicep` declares Key Vault reference `visitor-hash-secret` on API using `id-shortener-api`, maps `VisitorHash__DailySecret` to that reference, and grants Key Vault Secrets User at only the `visitor-hash-secret` scope.
- `infra/p2-visitor-hash-access.bicep` isolates that role assignment from the larger P2 template. Its provider what-if showed exactly one Create: role assignment `6c5f533e-3193-5832-84c7-0e009e1d3d1d` under the individual secret scope. No other change.
- `infra/scripts/deploy-visitor-hash-api.py` uses the live API definition to preserve its current image, settings, identity, and unrelated references. Its provider what-if showed exactly one Modify on `ca-shortener-api`: `properties.configuration.secrets` and `properties.template.containers`. No Create or Delete. The script appends only the named Key Vault reference and environment secret reference.
- The operator `social@mediaraq.com` has subscription Owner but no Key Vault data-plane secret role. A temporary vault-scoped Key Vault Secrets Officer assignment is required for the specifically requested `az keyvault secret set --file`. A temporary Bicep what-if showed exactly one Create: operator role assignment `cb65f404-ca4c-5b42-8de4-455654616247` under the vault. It must be removed after the secret write. This is an operator prerequisite, not an API grant.
- The vault currently has `defaultAction: Deny`, `bypass: AzureServices`, one ACA subnet rule, and no operator IP rules. The current observed operator public IP was `<operator-ip>`; recheck it at execution time. A provider what-if using the live vault configuration plus only this `/32` showed one vault Modify with the single delta `properties.networkAcls.ipRules` Create. The existing temporary firewall procedure adds only the current `/32`, then removes it in guaranteed cleanup and verifies `ipRules` is empty. The vault network ACL's other properties stay unchanged.
- ARM control-plane lookup returned ResourceNotFound for `visitor-hash-secret`; the CLI data-plane read returned ForbiddenByRbac. Neither printed a secret value. The API currently has neither the Key Vault reference nor `VisitorHash__DailySecret`.

## Approval gate and execution order

Michael requested the what-if before approval. **No Key Vault secret, operator role, firewall rule, API role, or API setting has been written yet.** After approval, recheck account, subscription, lock, vault ACL, operator IP, and the two provider what-ifs. Then:

1. Grant the temporary operator Secrets Officer role, wait for data-plane access, and add only the current operator `/32` Key Vault IP rule. Use a guarded cleanup path to remove that rule and temporary operator role on success or failure.
2. With `umask 077`, create a temporary file containing a URL-safe textual encoding of 48 cryptographically random bytes, without a newline. Call `az keyvault secret set --vault-name kv-shortener-f2xpl6 --name visitor-hash-secret --file <temp-file> --encoding utf-8` with output suppressed. Delete the temporary file immediately, including on failure. Never print or log its contents. Verify only secret metadata through the control plane.
3. Restore the firewall and temporary operator access; verify the vault's IP rule list is empty. Deploy the single secret-scoped API Secrets User role from `p2-visitor-hash-access.bicep` and verify its scope and principal. Allow RBAC propagation, then deploy the focused API reference from `deploy-visitor-hash-api.py`. Confirm the current API image remains unchanged, the app reaches a healthy revision, and the two reference names are present without reading the value. Report steps 1–2 as deployed only after those checks pass.

## Later Track G rollout

Read Track G's validation and rollout at `/Users/michaelinman/Repos/SchwabUrlShortener-insights/docs/2026-10-14-0952-track-g-link-insights-validation.md`. That branch is not yet on `main`; its own release gate requires confirmation of the secret and API reference first. Once Track G is on `main`, build/pin new API, Analytics, and Web digests; run `caj-shortener-migrate` with the new API digest and require a successful exit before changing the serving API. Then update API, Analytics, and Web to the approved digests, smoke-test the public path and guest/managed behavior, inspect existing click-consumed, deduplicated, outbox-age, and consumer-lag signals, and verify a signed-in managed link's Insights page in Azure. Local Track G tests do not establish these production outcomes.

## Acceptance and current outcome

- [x] Account, subscription, vault, identity, lock, and current API configuration checked.
- [x] P2 and focused Bicep compiled; focused API script syntax checked; secret-scope role, API, temporary operator-role, and temporary firewall provider what-ifs obtained.
- [ ] Michael approves the previews and temporary operator prerequisite.
- [ ] Secret is written through a protected temp file; operator firewall and role are restored.
- [ ] API secret-scoped role and API reference are deployed and live-verified; then Track G is cleared to merge.
- [ ] After Track G reaches `main`, migration-first image rollout and live Insights acceptance complete.

Current status: **previewed only; awaiting Michael's approval before Azure writes.**
