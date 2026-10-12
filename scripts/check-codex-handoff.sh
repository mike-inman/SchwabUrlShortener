#!/usr/bin/env bash
set -euo pipefail

# Ported from AI-Influencer-Factory's gated handoff checker for this repository.
# Checks section presence and order, the repository path, and unfinished fields.

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <handoff-file>"
  exit 2
fi

file="$1"
if [[ ! -f "$file" ]]; then
  echo "error: handoff file not found: $file"
  exit 2
fi

errors=0
last_line=0
require_in_order() {
  local label="$1"
  local pattern="$2"
  local line
  line="$(grep -nEm1 "$pattern" "$file" | cut -d: -f1 || true)"
  if [[ -z "$line" ]]; then
    echo "missing required section: $label"
    errors=1
  elif (( line <= last_line )); then
    echo "section out of order: $label"
    errors=1
  else
    last_line="$line"
  fi
}

require_in_order 'title (## <name> Handoff)' '^## .+ Handoff[[:space:]]*$'
require_in_order 'status date' '^Status: drafted on [0-9]{4}-[0-9]{2}-[0-9]{2}[[:space:]]*$'
require_in_order 'working directory line' '^You are working in /Users/michaelinman/Repos/SchwabUrlShortener\.[[:space:]]*$'
require_in_order 'repo standards' '^Follow these repo standards exactly:[[:space:]]*$'
require_in_order 'task line' '^Task:[[:space:]]*.+$'
require_in_order 'goal' '^Goal:[[:space:]]*$'
require_in_order 'important context' '^Important context:[[:space:]]*$'
require_in_order 'in-scope' '^In scope only:[[:space:]]*$'
require_in_order 'out-of-scope' '^Out of scope:[[:space:]]*$'
require_in_order 'required discovery' '^Required discovery before edits:[[:space:]]*$'
require_in_order 'implementation requirements' '^Implementation requirements:[[:space:]]*$'
require_in_order 'exception / observability requirements' '^Exception / observability requirements:[[:space:]]*$'
require_in_order 'realtime / no-polling requirements' '^Realtime / no-polling requirements:[[:space:]]*$'
require_in_order 'testing' '^Testing:[[:space:]]*$'
require_in_order 'documentation' '^Documentation:[[:space:]]*$'
require_in_order 'completion criteria' '^Completion criteria:[[:space:]]*$'

if ! grep -Eq '^[1-9][0-9]*\. .+' "$file"; then
  echo 'missing numbered in-scope item: 1. ...'
  errors=1
fi
if ! grep -Eq '^- \[ \] .+' "$file"; then
  echo 'missing completion checklist item: - [ ] ...'
  errors=1
fi
if grep -Fq '{{' "$file"; then
  echo 'unfinished template placeholder: {{...}}'
  errors=1
fi

if [[ "$errors" -ne 0 ]]; then
  echo "handoff validation failed: $file"
  exit 1
fi

echo "handoff validation passed: $file"
