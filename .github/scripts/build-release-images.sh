#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/deploy-common.sh"

[[ -f $deployment_state ]] || fail_deploy 'previous image state was not recorded'
context=$RUNNER_TEMP/shortener-build-context
mkdir -p "$context"
git archive "$DEPLOY_SHA" Dockerfile src | tar -x -C "$context"
tag=git-${DEPLOY_SHA:0:12}

build_one() {
  local target=$1 project=$2 digest image
  az acr build --registry "$ACR_NAME" --image "shortener-$target:$tag" \
    --target "$target" --build-arg "PROJECT=$project" --file Dockerfile "$context" --output none \
    || fail_deploy "ACR build failed for $target"
  digest=$(az acr repository show --name "$ACR_NAME" --image "shortener-$target:$tag" --query digest --output tsv)
  image="$ACR_NAME.azurecr.io/shortener-$target@$digest"
  require_digest_image "$image" "$target"
  printf 'NEW_%s=%s\n' "${target^^}" "$image" >> "$deployment_images"
  summary_line "- New $target: \`$image\`"
}

: > "$deployment_images"
build_one api src/Shortener.Api/Shortener.Api.csproj
build_one analytics src/Shortener.Analytics/Shortener.Analytics.csproj
build_one web src/Shortener.Web/Shortener.Web.csproj
