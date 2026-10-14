#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/deploy-common.sh"

[[ -f $deployment_state && -f $deployment_images ]] || fail_deploy 'migration image records are missing'
source "$deployment_state"
source "$deployment_images"
require_digest_image "$NEW_API" migration

restore_job_on_failure() {
  az containerapp job update --name "$deployment_job" --resource-group "$deployment_group" \
    --image "$PREVIOUS_MIGRATION" --output none || summary_line '- Previous migration job image could not be restored.'
}

az containerapp job update --name "$deployment_job" --resource-group "$deployment_group" \
  --image "$NEW_API" --output none || {
    restore_job_on_failure
    fail_deploy 'migration job image update failed'
  }
execution=$(az containerapp job start --name "$deployment_job" --resource-group "$deployment_group" \
  --query name --output tsv) || {
  restore_job_on_failure
  fail_deploy 'migration job could not start'
}
[[ -n $execution && $execution != None ]] || {
  restore_job_on_failure
  fail_deploy 'migration job did not return an execution name'
}
summary_line "- Migration execution: \`$execution\`"

for attempt in {1..90}; do
  status=$(az containerapp job execution show --name "$deployment_job" --resource-group "$deployment_group" \
    --job-execution-name "$execution" --query properties.status --output tsv) || {
    restore_job_on_failure
    fail_deploy "migration execution $execution status query failed"
  }
  case "$status" in
    Succeeded)
      summary_line "- Migration: \`$execution\` Succeeded. Schema changes must be backward-compatible."
      exit 0 ;;
    Failed|Canceled|Cancelled)
      restore_job_on_failure
      fail_deploy "migration execution $execution ended as $status" ;;
  esac
  if (( attempt % 3 == 0 )); then
    printf 'Migration execution %s: %s\n' "$execution" "$status"
  fi
  sleep 10
done
restore_job_on_failure
fail_deploy "migration execution $execution did not finish within 15 minutes (last status: $status)"
