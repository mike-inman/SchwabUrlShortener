#!/bin/sh
# Reconcile only the customer Web registration's redirect URIs through the
# separate External ID Azure CLI profile.
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec python3 "$script_dir/configure-entra.py" "$@"
