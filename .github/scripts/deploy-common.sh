#!/usr/bin/env bash
set -Eeuo pipefail

deployment_group=${AZURE_RESOURCE_GROUP:?AZURE_RESOURCE_GROUP is required}
deployment_job=caj-shortener-migrate
deployment_state=${RUNNER_TEMP:?RUNNER_TEMP is required}/shortener-deploy-state.env
deployment_images=${RUNNER_TEMP}/shortener-deploy-images.env
deployment_started=${RUNNER_TEMP}/shortener-update-started

trap 'printf "%s\n" "- Failed: deployment command failed at line $LINENO." >> "${GITHUB_STEP_SUMMARY:-/dev/null}"' ERR

summary_line() {
  printf '%s\n' "$*" >> "${GITHUB_STEP_SUMMARY:?GITHUB_STEP_SUMMARY is required}"
}

fail_deploy() {
  summary_line "- Failed: $*"
  printf 'Deployment failed: %s\n' "$*" >&2
  exit 1
}

require_digest_image() {
  [[ $1 =~ ^[a-z0-9.-]+\.azurecr\.io/[a-z0-9/_-]+@sha256:[0-9a-f]{64}$ ]] || fail_deploy "invalid digest image for $2"
}

wait_revision() {
  local app=$1 revision=$2 state health attempt row
  for attempt in {1..60}; do
    row=$(az containerapp revision show --name "$app" --revision "$revision" \
      --resource-group "$deployment_group" --output json |
      jq -er --arg revision "$revision" -f .github/scripts/revision-status.jq) \
      || fail_deploy "$app revision status query or parse failed"
    read -r state health <<< "$row"
    if [[ $state == Running && $health == Healthy ]]; then
      summary_line "- $app revision: \`$revision\` (Running, Healthy)"
      return 0
    fi
    if [[ $state == Failed || $health == Unhealthy ]]; then
      fail_deploy "$app revision $revision is $state/$health"
    fi
    if (( attempt % 3 == 0 )); then
      printf '%s revision %s: %s/%s\n' "$app" "$revision" "${state:-pending}" "${health:-pending}"
    fi
    sleep 10
  done
  fail_deploy "$app revision $revision did not become healthy within 10 minutes"
}

update_and_wait() {
  local app=$1 image=$2 revision current
  require_digest_image "$image" "$app"
  az containerapp update --name "$app" --resource-group "$deployment_group" --image "$image" --output none \
    || fail_deploy "$app image update failed"
  revision=$(az containerapp show --name "$app" --resource-group "$deployment_group" \
    --query properties.latestRevisionName --output tsv)
  [[ -n $revision && $revision != None ]] || fail_deploy "$app returned no new revision"
  current=$(az containerapp show --name "$app" --resource-group "$deployment_group" \
    --query 'properties.template.containers[0].image' --output tsv)
  [[ $current == "$image" ]] || fail_deploy "$app does not reference the requested digest"
  wait_revision "$app" "$revision"
}
