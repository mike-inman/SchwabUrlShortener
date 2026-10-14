#!/bin/bash
set -euo pipefail

group=rg-schwab-url-shortener
job=caj-shortener-migrate
execution=$(az containerapp job start --name "$job" --resource-group "$group" --query name --output tsv)
if [[ -z "$execution" || "$execution" == None ]]; then
  echo 'Migration job did not return an execution name.' >&2
  exit 1
fi

for attempt in {1..90}; do
  status=$(az containerapp job execution show --name "$job" --resource-group "$group" --job-execution-name "$execution" --query properties.status --output tsv)
  case "$status" in
    Succeeded) echo "Migration execution $execution succeeded."; exit 0 ;;
    Failed|Canceled|Cancelled) echo "Migration execution $execution ended as $status." >&2; exit 1 ;;
  esac
  sleep 10
done
echo "Migration execution $execution did not finish within 15 minutes." >&2
exit 1
