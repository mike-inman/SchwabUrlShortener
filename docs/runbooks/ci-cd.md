# CI/CD application delivery runbook — 2026-10-14 09:24 CDT

## Boundary and current gate

`ci.yml` runs on pull requests, pushes to `main`, and the weekly CodeQL schedule. The deploy workflow receives completed CI runs for `main` but starts only after a **successful push** CI run from this repository, or a manual dispatch on `main`. It checks out the successful CI commit and rejects a commit that is no longer the tip of `main`. `DEPLOY_ENABLED` must be `true`; the `production` environment must require Michael's approval before the job can request an OIDC token.

As of this document's date the repository is private, has no GitHub environments, and branch protection returns HTTP 403 on the current plan. The Azure deployment identity, GitHub settings, production approval, deployment and rollback proof are **not yet configured or verified**. Keep `DEPLOY_ENABLED` unset or `false` until the repository is public and the reviewed setup below succeeds. An environment name alone does not create an approval gate.

The routine workflow is application delivery only. It never runs `azd provision`, Bicep what-if, or the Entra postprovision hook. Michael applies infrastructure changes from his machine with `azd provision --preview`, reviews the result, and then runs `azd provision` under the Track C runbook. This keeps PostgreSQL, Redis, and Web client secrets outside GitHub and Graph rights off the pipeline identity. CI still builds and lints Bicep when `infra/main.bicep` is committed.

## CI gates

Required PR checks: `build-and-test`, `integration-and-startup`, `vulnerabilities`, `bicep`, `codeql`, `dependency-review`, `secret-scan`, `image-scan-api`, `image-scan-analytics`, and `image-scan-web`. Dependency review is PR-only because GitHub's dependency review action compares a PR with its base. Pushes to `main` run the other gates. The secret job fetches full history and runs `gitleaks git --log-opts=--all`; do not add a `.gitleaks.toml` unless a finding is proven false and its exception is commented. The three Trivy jobs build the actual Dockerfile targets, fail on fixable High/Critical findings, and upload SARIF when code scanning accepts it. Dependabot checks NuGet, GitHub Actions, and Docker weekly.

The integration job uses only checked-in placeholder configuration, a disposable `Schwab_test_local` database, and its own Compose project. For local Track F validation use a unique project name and a temporary network override with `10.251.47.0/24`; inspect existing networks before starting. Do not stop or remove any other Compose project or volume.

## Reviewed Azure identity setup — do not run before Michael approves

The pipeline needs the user-assigned identity `id-github-deploy-ziplio` in `rg-schwab-url-shortener`. Its federated credential must match issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`, and subject `repo:mike-inman/SchwabUrlShortener:environment:production`. Record the created client ID here after approval: **pending**. It is not a secret.

Run the following only after confirming `az account show` reports subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e` and tenant `eea269ab-1571-48a5-a18c-824ba5859cd5`:

```bash
az account show --query '{subscription:id,tenant:tenantId,user:user.name}' -o table
group=rg-schwab-url-shortener
identity=id-github-deploy-ziplio
az identity create -g "$group" -n "$identity" --location centralus --output none
client_id=$(az identity show -g "$group" -n "$identity" --query clientId -o tsv)
principal_id=$(az identity show -g "$group" -n "$identity" --query principalId -o tsv)
az identity federated-credential create -g "$group" --identity-name "$identity" \
  --name github-production --issuer https://token.actions.githubusercontent.com \
  --subject repo:mike-inman/SchwabUrlShortener:environment:production \
  --audiences api://AzureADTokenExchange --output none
group_id=$(az group show -n "$group" --query id -o tsv)
acr_id=$(az acr show -g "$group" -n acrshortenerf2xpl6 --query id -o tsv)
az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
  --role Reader --scope "$group_id" --output none
az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
  --role AcrPush --scope "$acr_id" --output none
az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
  --role 'Container Registry Tasks Contributor' --scope "$acr_id" --output none
for app in ca-shortener-api ca-shortener-analytics ca-shortener-web; do
  app_id=$(az containerapp show -g "$group" -n "$app" --query id -o tsv)
  az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
    --role 'Container Apps Contributor' --scope "$app_id" --output none
done
job_id=$(az containerapp job show -g "$group" -n caj-shortener-migrate --query id -o tsv)
az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
  --role 'Container Apps Jobs Contributor' --scope "$job_id" --output none
printf 'Deployment identity client ID: %s\n' "$client_id"
```

`AcrPush` permits image writes and digest lookup. ACR Tasks Contributor permits `az acr build` (`scheduleRun/action` and build source upload); AcrPush alone does not. Container Apps Contributor permits the three app updates, scoped to those resources. The separate Jobs Contributor role permits updating and starting the one migration job; Container Apps Contributor does not grant `jobs/start`. Reader at resource-group scope supports discovery. These are the narrowest relevant built-in roles found through `az role definition list` on 2026-10-14. No Key Vault, Graph, Owner, or User Access Administrator role is assigned. Verify actual scope and assignments after creation.

## Reviewed GitHub setup — after the repository is public

Inspect `.github/scripts/configure-repo-protection.sh` and the currently configured GitHub settings. Michael approves the exact script invocation before it runs:

```bash
AZURE_CLIENT_ID=<approved-client-id> bash .github/scripts/configure-repo-protection.sh
```

The script refuses a private repository, creates `production` with `mike-inman` as required reviewer (`prevent_self_review=false`) and a `main`-only deployment branch policy, sets the nonsecret Azure and origin variables, requires PRs and all PR checks on `main`, disallows force pushes/deletion, and enables secret scanning with push protection. It removes only three obsolete **GitHub Actions** secrets (`POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `WEB_CLIENT_SECRET`) after the workflow no longer references them. It does not touch local passwords or Key Vault secrets. It sets `DEPLOY_ENABLED=true` last. If any settings operation fails, leave deployment disabled and inspect the partial settings before retrying. Confirm the environment reviewer and branch policy in GitHub's UI before relying on an approval gate.

## Deployment and approval

1. Merge a fully green PR to `main` only after checking the listed gates. `workflow_run` queues application deployment when that push's CI completes successfully. A `pull_request`, fork, failed CI, stale `main` SHA, or non-main manual dispatch cannot deploy.
2. Open **Actions → Deploy production** for the run. Review the commit and release intent, then Michael approves the `production` environment request. Approval happens before Azure login, ACR builds, migrations, and app updates.
3. The job records the current API, Analytics, Web, and migration job digests; builds three images with Dockerfile targets in ACR; resolves immutable digests; updates and runs the migration job; updates API, Analytics, then Web, waiting up to ten minutes for each new revision to be Running and Healthy; and checks public liveness, readiness, guest create and redirect, www-to-apex redirect, and favicon. The step summary records commit, digests, revision names, smoke results, and rollback status. Migration execution names and status are reported without job logs.
4. Migrations must be backward-compatible: expand first, deploy compatible code, contract only in a later separately reviewed release. A failed migration blocks app updates and restores the previous **job image**. A failed app update or smoke check restores the previous application and job digests; it never automatically reverses database schema changes. The workflow remains failed even if image rollback succeeds.

For a deliberate rollback drill, use **Run workflow** on `main` with `smoke_expect_fail=true` only after Michael approves the production run. The script performs the normal public checks, then fails intentionally; the rollback step must restore the recorded previous digests. Inspect the step summary and Azure revision image references afterward. The input is restricted to manual dispatch and is retained only as a documented test switch.

## Manual recovery

If automated rollback fails, read the previous digests in the failed run's step summary. Review migration compatibility and current app state, then update each app and the migration job to its recorded digest with `az containerapp update --image` and `az containerapp job update --image` under an approved operator action. Wait for each app's latest revision to be Running and Healthy; run the public smoke script again. Do not attempt a reverse database migration without a separate recovery plan and backup review. Track C's image rollback runbook in its infrastructure branch has environment-specific steps; reconcile it when that branch merges.
