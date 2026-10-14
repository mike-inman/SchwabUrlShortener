#!/bin/sh
# Reconcile only this deployment's Web app redirect URIs. Federation is opt-in
# until its separate live sign-in validation is approved.
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec python3 "$script_dir/configure-entra.py" "$@"
