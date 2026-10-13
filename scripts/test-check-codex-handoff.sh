#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
checker="$repo_root/scripts/check-codex-handoff.sh"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT

cat > "$test_dir/valid.md" <<'HANDOFF'
## Example Handoff
Status: drafted on 2026-10-12
You are working in /Users/michaelinman/Repos/SchwabUrlShortener.
Follow these repo standards exactly:
- Preserve unrelated work and credentials.
Task: Validate the handoff checker.
Goal:
Require the gated format.
Important context:
This is a test fixture.
In scope only:
1. Validate section detection.
Out of scope:
- Runtime work.
Required discovery before edits:
- Read the checker.
Implementation requirements:
- Keep the fixture minimal.
Exception / observability requirements:
- No runtime path is touched.
Realtime / no-polling requirements:
- Not applicable to this fixture.
Testing:
- Run the checker.
Documentation:
- None.
Completion criteria:
- [ ] The checker accepts this fixture.
HANDOFF

bash "$checker" "$test_dir/valid.md" >/dev/null

expect_failure() {
  local file="$1"
  local expected="$2"
  if bash "$checker" "$file" >"$test_dir/result.out" 2>&1; then
    echo "expected invalid handoff to fail: $file"
    exit 1
  fi
  if ! grep -Fq "$expected" "$test_dir/result.out"; then
    echo "wrong diagnostic for: $file"
    cat "$test_dir/result.out"
    exit 1
  fi
}

sed '/^Exception \/ observability requirements:$/d' "$test_dir/valid.md" > "$test_dir/missing.md"
expect_failure "$test_dir/missing.md" 'missing required section: exception / observability requirements'

awk '/^Testing:$/ { print "Documentation:"; next } /^Documentation:$/ { print "Testing:"; next } { print }' "$test_dir/valid.md" > "$test_dir/out-of-order.md"
expect_failure "$test_dir/out-of-order.md" 'section out of order: documentation'

sed 's|/Users/michaelinman/Repos/SchwabUrlShortener|/Users/michaelinman/Repos/OtherProject|' "$test_dir/valid.md" > "$test_dir/wrong-repo.md"
expect_failure "$test_dir/wrong-repo.md" 'working directory is not a registered repository worktree'

sed 's/2026-10-12/{{DATE}}/' "$test_dir/valid.md" > "$test_dir/placeholder.md"
expect_failure "$test_dir/placeholder.md" 'unfinished template placeholder'

sed '/^- \[ \] /d' "$test_dir/valid.md" > "$test_dir/no-checklist.md"
expect_failure "$test_dir/no-checklist.md" 'missing completion checklist item'

echo 'check-codex-handoff self-test passed'
