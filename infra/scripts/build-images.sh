#!/bin/sh
set -eu

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd -P)
cd "$repo_root"

if ! command -v az >/dev/null 2>&1 || ! command -v azd >/dev/null 2>&1; then
  printf '%s\n' 'az and azd are required.' >&2
  exit 2
fi

if [ -n "$(git status --porcelain --untracked-files=normal)" ]; then
  printf '%s\n' 'Commit source changes before building immutable release images.' >&2
  exit 2
fi

# Only committed application source enters the remote ACR build context. In
# particular, the local azd environment under .azure/ must never be uploaded.
build_context=$(mktemp -d)
trap 'rm -rf "$build_context"' EXIT HUP INT TERM
source_ref=${IMAGE_SOURCE_REF:-HEAD}
source_commit=$(git rev-parse --verify "${source_ref}^{commit}")
git archive "$source_commit" Dockerfile src | tar -x -C "$build_context"

registry=${AZURE_CONTAINER_REGISTRY_NAME:-}
if [ -z "$registry" ]; then
  registry=$(azd env get-value AZURE_CONTAINER_REGISTRY_NAME 2>/dev/null || true)
fi
if [ -z "$registry" ]; then
  printf '%s\n' 'AZURE_CONTAINER_REGISTRY_NAME is required; provision the foundation first.' >&2
  exit 2
fi

revision=$(git rev-parse --short=12 "$source_commit")
build_one() {
  stage=$1
  project=$2
  setting=$3
  repository="shortener-$stage"
  tag="git-$revision"
  digest=$(az acr repository show --name "$registry" --image "$repository:$tag" --query digest -o tsv 2>/dev/null || true)
  if [ -z "$digest" ]; then
    az acr build \
      --registry "$registry" \
      --image "$repository:$tag" \
      --target "$stage" \
      --build-arg "PROJECT=$project" \
      --file Dockerfile "$build_context" >/dev/null
    digest=$(az acr repository show \
      --name "$registry" \
      --image "$repository:$tag" \
      --query digest -o tsv)
  fi
  if ! printf '%s\n' "$digest" | grep -Eq '^sha256:[0-9a-fA-F]{64}$'; then
    printf 'Invalid image digest for %s.\n' "$repository" >&2
    exit 1
  fi
  azd env set "$setting" "$registry.azurecr.io/$repository@$digest" >/dev/null
  printf 'Built %s at %s\n' "$repository" "$digest"
}

build_one api src/Shortener.Api/Shortener.Api.csproj SERVICE_API_IMAGE
build_one web src/Shortener.Web/Shortener.Web.csproj SERVICE_WEB_IMAGE
build_one analytics src/Shortener.Analytics/Shortener.Analytics.csproj SERVICE_ANALYTICS_IMAGE
