# CI/CD runbook — 2026-10-13 19:45 CDT

## Purpose and current state

`ci.yml` runs on pull requests and pushes to `main`. It builds the .NET 10 solution with warnings as errors, runs unit and Web tests with TRX artifacts, runs the full integration suite against a fresh Compose project with the checked-in placeholder environment, checks each real host's startup from committed `appsettings.json`, audits direct and transitive NuGet packages for High/Critical advisories, and builds/lints Bicep when `infra/main.bicep` reaches the branch. The startup check permits only disposable Compose service endpoints plus the API's documented placeholder client ID and Analytics' Compose topic/group identifiers. It removes all Compose application behavior overrides, so a missing `LiveUpdates` default fails startup. The Web's guest-mode startup does not require a client secret.

`deploy.yml` runs on `main` push or manual dispatch but its job is skipped unless repository variable `DEPLOY_ENABLED` is exactly `true`. It also requires the `production` environment, so an environment reviewer approves each run before any Azure token is issued. It uses GitHub OIDC for both Azure CLI and `azd`, previews Bicep changes in the job summary with resource IDs only, provisions, builds ACR images by digest through Track C's `predeploy` hook, deploys, waits for the migration job to succeed, and smoke-tests public readiness, guest creation, and redirect. The current Track C checkout has only P1 infrastructure and `deploymentPhase` accepts only `p1`; the workflow's P2 preflight intentionally blocks deployment until Track C merges a deployable P2 and migration job. No deployment has run from Track F.

## Michael's setup before enabling deployment

1. Confirm Track C's P2 Bicep, `azure.yaml`, image-build hook, Container Apps, and `caj-shortener-migrate` have merged into `main`; inspect its approved what-if and runbook. Confirm the public HTTPS origin and its `/health/ready` route. Do not enable this workflow on the P1-only template.
2. In `mike-inman/SchwabUrlShortener` → **Settings → Environments**, create `production`. Add Michael as a **required reviewer**, restrict deployments to `main`, and disable administrator bypass if available. Do not select “prevent self-review” if Michael will dispatch and approve his own run. This repository is private; GitHub plan and repository visibility must support required reviewers. If the setting is unavailable, keep `DEPLOY_ENABLED=false` and use Track C's operator-approved manual deployment process. Merely naming `production` in a workflow can create an unprotected environment.
3. Create the dedicated deployment application and federated credential using the commands below. Run them yourself after confirming subscription, tenant, and resource group; Track F does not execute Azure identity or role changes. The application has no client secret.
4. Set repository variables `AZURE_CLIENT_ID` (the new application ID), `AZURE_TENANT_ID=eea269ab-1571-48a5-a18c-824ba5859cd5`, `AZURE_SUBSCRIPTION_ID=baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, `PUBLIC_ORIGIN` (the public HTTPS origin), `BUDGET_CONTACT_EMAIL`, `API_CLIENT_ID`, and `WEB_CLIENT_ID`. Set `DEPLOY_ENABLED=false` initially.
5. Set `production` environment secrets `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, and `WEB_CLIENT_SECRET`. The current P1 template and this workflow require all three on each provision; until Track C makes secure parameters optional on update, preserve the existing values and supply them privately. Do not echo, paste into a workflow log, or rotate them during enablement. If a verified managed-identity replacement removes the Web secret later, update this workflow and runbook together.
6. Once CI passes on the merged commit and the P2 preflight plus dry run are reviewed, set `DEPLOY_ENABLED=true`. A `main` push or **Actions → Deploy production → Run workflow** queues the job. Open its run, inspect the GitHub environment approval request, and approve only after reviewing the commit and expected deployment. The workflow's own what-if summary appears after approval and before provisioning; if separate operator review of the what-if is required, keep the workflow disabled and run the approved Track C process instead.

### Dedicated deployment identity — operator commands

These commands create only the Entra application, its GitHub trust, and resource-group-scoped Azure roles. They do not grant subscription-wide authority. Review the IDs and group before running. The role assignments are required by Track C's P1 Bicep, which creates resource identities and scoped role grants. Re-evaluate the `User Access Administrator` role after P2 if the final template can use a narrower role assignment delegate.

```bash
az account set --subscription baaf4acd-ff40-43d1-a7ba-6089a2c4f41e
az account show --query '{subscription:id,tenant:tenantId}' --output table
az group show --name rg-schwab-url-shortener --query id --output tsv

app_id=$(az ad app create --display-name github-schwab-url-shortener-deploy --query appId --output tsv)
az ad sp create --id "$app_id" --output none
app_object_id=$(az ad app show --id "$app_id" --query id --output tsv)
cat > /tmp/schwab-github-oidc.json <<'JSON'
{
  "name": "github-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:mike-inman/SchwabUrlShortener:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}
JSON
az ad app federated-credential create --id "$app_object_id" --parameters /tmp/schwab-github-oidc.json --output none
rm /tmp/schwab-github-oidc.json

resource_group_id=$(az group show --name rg-schwab-url-shortener --query id --output tsv)
principal_id=$(az ad sp show --id "$app_id" --query id --output tsv)
az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal --role Contributor --scope "$resource_group_id" --output none
az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal --role 'User Access Administrator' --scope "$resource_group_id" --output none
printf 'Set AZURE_CLIENT_ID=%s as a repository variable.\n' "$app_id"
```

The commands do not grant rights for subscription-scope resources such as Defender for Cloud pricing or resource-group creation. Those remain Track C's separately approved operator operations. The existing resource group must already exist. `azd provision` must remain scoped to that group.

## Rollback and credential rotation

Record each deployed Git commit and the three ACR image digests in the deployment record. For rollback, dispatch `Deploy production` against `main` only after reverting or restoring the previous commit on `main`; workflow dispatch cannot safely choose an arbitrary old ref under the current `main` guard. The build hook reuses the immutable `git-<commit>` tags and resolves their digests. Confirm the target Bicep and migration compatibility first, then approve the environment job and smoke-test it. Do not reverse a database migration automatically. Track C's [image rollback runbook](2026-10-13-1935-image-rollback.md) governs any direct digest rollback after P2 merges.

To rotate the GitHub trust, create a second federated credential with a new name but the same exact issuer, audience, and environment subject. Verify one approved workflow run using the new credential, then remove the old credential by its ID. If the application itself is replaced, first create a replacement app and role assignments, point `AZURE_CLIENT_ID` to it, verify a run, then retire the old app. Never delete existing local passwords or Key Vault secret versions as part of this workflow change.

## Sources checked 2026-10-13

- [GitHub OIDC with Azure](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure)
- [GitHub deployment environments and protection](https://docs.github.com/en/actions/concepts/workflows-and-actions/deployment-environments)
- [GitHub reviewer availability by plan and visibility](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/review-deployments)
- [Microsoft azd GitHub Actions guidance](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/pipeline-github-actions)
- [Microsoft azd authentication reference](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/reference)
- [Microsoft Container Apps jobs](https://learn.microsoft.com/en-us/azure/container-apps/jobs)
