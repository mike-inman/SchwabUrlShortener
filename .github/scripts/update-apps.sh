#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/deploy-common.sh"

[[ -f $deployment_state && -f $deployment_images ]] || fail_deploy 'deployment image records are missing'
source "$deployment_images"
touch "$deployment_started"
update_and_wait ca-shortener-api "$NEW_API"
update_and_wait ca-shortener-analytics "$NEW_ANALYTICS"
update_and_wait ca-shortener-web "$NEW_WEB"
