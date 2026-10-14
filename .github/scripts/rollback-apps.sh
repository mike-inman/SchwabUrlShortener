#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/deploy-common.sh"

[[ -f $deployment_started ]] || exit 0
[[ -f $deployment_state ]] || fail_deploy 'rollback state is missing'
source "$deployment_state"
summary_line '- Rollback: started after application update or smoke failure.'
failed=0
for pair in "ca-shortener-api:$PREVIOUS_API" "ca-shortener-analytics:$PREVIOUS_ANALYTICS" "ca-shortener-web:$PREVIOUS_WEB"; do
  app=${pair%%:*}
  image=${pair#*:}
  if ! (update_and_wait "$app" "$image"); then
    summary_line "- Rollback failed for $app."
    failed=1
  fi
done
if ! az containerapp job update --name "$deployment_job" --resource-group "$deployment_group" --image "$PREVIOUS_MIGRATION" --output none; then
  summary_line '- Rollback failed for migration job image.'
  failed=1
fi
if (( failed )); then
  fail_deploy 'one or more previous images could not be restored; operator action required'
fi
summary_line '- Rollback: previous application and migration job digests restored. Database migration was not reversed.'
