#!/usr/bin/env bash
# Reviewed operator step. Run only after the repository is public and Michael approves.
set -euo pipefail

repo=mike-inman/SchwabUrlShortener
environment=production
client_id=${AZURE_CLIENT_ID:?Set the approved deployment identity client ID}
[[ $client_id =~ ^[0-9a-fA-F-]{36}$ ]] || { echo 'AZURE_CLIENT_ID is not a GUID.' >&2; exit 1; }
[[ $(gh api "repos/$repo" --jq .visibility) == public ]] || {
  echo 'Repository is not public; no settings were changed.' >&2
  exit 1
}
[[ $(az account show --query id -o tsv) == baaf4acd-ff40-43d1-a7ba-6089a2c4f41e ]] || {
  echo 'Azure CLI is not using the reviewed subscription; no settings were changed.' >&2
  exit 1
}
[[ $(az identity show -g rg-schwab-url-shortener -n id-github-deploy-ziplio --query clientId -o tsv) == "$client_id" ]] || {
  echo 'AZURE_CLIENT_ID does not match the reviewed Azure identity; no settings were changed.' >&2
  exit 1
}

gh variable set DEPLOY_ENABLED --repo "$repo" --body false

reviewer_id=$(gh api users/mike-inman --jq .id)
jq -n --argjson reviewer "$reviewer_id" '{wait_timer:0,prevent_self_review:false,
  reviewers:[{type:"User",id:$reviewer}],
  deployment_branch_policy:{protected_branches:false,custom_branch_policies:true}}' |
  gh api -X PUT "repos/$repo/environments/$environment" --input - --silent

policies=$(gh api "repos/$repo/environments/$environment/deployment-branch-policies" --jq '[.branch_policies[].name] | join(",")')
if [[ -z $policies ]]; then
  gh api -X POST "repos/$repo/environments/$environment/deployment-branch-policies" -f name=main --silent
elif [[ $policies != main ]]; then
  echo 'Production has unexpected branch policies; review them before enabling deployment.' >&2
  exit 1
fi

gh variable set AZURE_CLIENT_ID --repo "$repo" --body "$client_id"
gh variable set AZURE_TENANT_ID --repo "$repo" --body eea269ab-1571-48a5-a18c-824ba5859cd5
gh variable set AZURE_SUBSCRIPTION_ID --repo "$repo" --body baaf4acd-ff40-43d1-a7ba-6089a2c4f41e
gh variable set AZURE_RESOURCE_GROUP --repo "$repo" --body rg-schwab-url-shortener
gh variable set ACR_NAME --repo "$repo" --body acrshortenerf2xpl6
gh variable set PUBLIC_ORIGIN --repo "$repo" --body https://ziplio.io

jq -n '{required_status_checks:{strict:true,contexts:[
  "build-and-test","integration-and-startup","vulnerabilities","bicep","codeql",
  "dependency-review","secret-scan","image-scan-api","image-scan-analytics","image-scan-web"]},
  enforce_admins:true,
  required_pull_request_reviews:{dismiss_stale_reviews:true,required_approving_review_count:0},
  restrictions:null,
  required_linear_history:true,
  allow_force_pushes:false,
  allow_deletions:false}' |
  gh api -X PUT "repos/$repo/branches/main/protection" --input - --silent

jq -n '{security_and_analysis:{secret_scanning:{status:"enabled"},
  secret_scanning_push_protection:{status:"enabled"}}}' |
  gh api -X PATCH "repos/$repo" --input - --silent

for name in POSTGRES_PASSWORD REDIS_PASSWORD WEB_CLIENT_SECRET; do
  if gh api "repos/$repo/actions/secrets/$name" --silent >/dev/null 2>&1; then
    gh api -X DELETE "repos/$repo/actions/secrets/$name" --silent
  fi
done
gh variable set DEPLOY_ENABLED --repo "$repo" --body true
echo 'Production reviewer, branch policy, required checks, scanning, and variables applied. Deployment enabled last.'
