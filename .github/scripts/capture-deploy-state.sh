#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/deploy-common.sh"

[[ -n ${ACR_NAME:-} && -n ${DEPLOY_SHA:-} ]] || fail_deploy 'ACR_NAME or DEPLOY_SHA is missing'
[[ $DEPLOY_SHA =~ ^[0-9a-f]{40}$ ]] || fail_deploy 'deployment commit is not a full SHA'
registry=$(az acr show --name "$ACR_NAME" --resource-group "$deployment_group" --query loginServer --output tsv)
[[ $registry == "$ACR_NAME.azurecr.io" ]] || fail_deploy 'registry name and login server differ'

running_image() {
  local app=$1 revision
  revision=$(az containerapp show --name "$app" --resource-group "$deployment_group" \
    --query properties.latestReadyRevisionName --output tsv)
  [[ -n $revision && $revision != None ]] || fail_deploy "$app has no ready revision to preserve"
  az containerapp revision list --name "$app" --resource-group "$deployment_group" \
    --query "[?name=='$revision'].properties.template.containers[0].image | [0]" --output tsv
}

api_image=$(running_image ca-shortener-api)
analytics_image=$(running_image ca-shortener-analytics)
web_image=$(running_image ca-shortener-web)
job_image=$(az containerapp job show --name "$deployment_job" --resource-group "$deployment_group" --query 'properties.template.containers[0].image' --output tsv)
for pair in "api:$api_image" "analytics:$analytics_image" "web:$web_image" "migration:$job_image"; do
  require_digest_image "${pair#*:}" "${pair%%:*}"
done

{
  printf 'PREVIOUS_API=%s\n' "$api_image"
  printf 'PREVIOUS_ANALYTICS=%s\n' "$analytics_image"
  printf 'PREVIOUS_WEB=%s\n' "$web_image"
  printf 'PREVIOUS_MIGRATION=%s\n' "$job_image"
} > "$deployment_state"

summary_line "### Application deployment \`$DEPLOY_SHA\`"
summary_line "- Previous API: \`$api_image\`"
summary_line "- Previous Analytics: \`$analytics_image\`"
summary_line "- Previous Web: \`$web_image\`"
summary_line "- Previous migration image: \`$job_image\`"
