#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "$0")/../.." && pwd)
parser="$repo_root/.github/scripts/revision-status.jq"
fixtures="$repo_root/tests/fixtures/revisions"

assert_status() {
  local revision=$1 expected_running=$2 expected_health=$3 actual expected running health
  actual=$(jq -er --arg revision "$revision" -f "$parser" "$fixtures/$revision.json")
  expected=$(printf '%s\t%s' "$expected_running" "$expected_health")
  [[ $actual == "$expected" ]] || {
    printf 'Wrong status for %s: %q (expected %q)\n' "$revision" "$actual" "$expected" >&2
    exit 1
  }
  IFS=$'\t' read -r running health <<< "$actual"
  [[ $running == "$expected_running" && $health == "$expected_health" ]] || {
    printf 'Bash could not read both statuses for %s.\n' "$revision" >&2
    exit 1
  }
}

# Sanitized projections of az containerapp revision show from production, 2026-10-14.
assert_status ca-shortener-web--0000008 Running Healthy
assert_status ca-shortener-analytics--0000002 Running Healthy
assert_status ca-shortener-api--0000010 Running Healthy
# This earlier API revision is now stopped; Healthy alone must not pass the gate.
assert_status ca-shortener-api--0000009 Stopped Healthy

if jq -er --arg revision wrong-name -f "$parser" "$fixtures/ca-shortener-web--0000008.json" >/dev/null 2>&1; then
  echo 'Parser accepted a different revision name.' >&2
  exit 1
fi

echo 'Revision status parser fixtures passed.'
