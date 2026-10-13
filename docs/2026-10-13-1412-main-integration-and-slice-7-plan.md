# Main integration and Slice 7 plan — 2026-10-13 14:12 CDT

Purpose: integrate the completed feature and Slice 6 work into `main`, publish it, then begin Slice 7 validation.

Scope: merge `codex/integrate-slices-1-3` at `09008b0` and `codex/workspace-authorization` at `ca6c2b7` into a clean `main` worktree; preserve all existing worktrees, untracked files, `.env` files, and secrets. Resolve overlaps in Compose, Web, API, and documentation without discarding either line of work.

Tasks and acceptance:

1. Review the merge base and every conflict. Keep both the customer identity/branding features and portable local/cloud Compose behavior.
2. Build and test the integrated solution, validate Compose variants, and record any unverified live gates.
3. Inspect staged files for credentials and unrelated local material. Commit the merge, push `main`, and verify its remote tip.
4. Start Slice 7 from the published integration baseline. Add a bounded, opt-in load script and dated validation evidence; do not infer a production capacity target.

Outcome (2026-10-13): Slice 6 was committed at `09008b0`. Both integration branches were merged in a separate clean `main` worktree. Conflicts in README, Compose, API routes, Web API address validation, and Web tests were resolved while preserving the portable Compose stack and the earlier existing-volume stack as `compose.existing.yaml`. The merged solution built with zero warnings/errors. Web tests passed 38/38 and unit tests 25/25. A disposable Compose project on port 18081 ran all 83 tests against real PostgreSQL, Redis, and Kafka: integration 20/20, unit 25/25, Web 38/38. Public `/` and `/health/ready` returned 200; guest creation returned 201 and `/r/mainchk7` redirected 302 to its test destination. The first container test run exposed four additional hard-coded loopback guards; a single guarded helper now admits only the named test database on the existing loopback port or an explicitly marked isolated Compose service. Live Entra, public DNS/TLS, cloud operations, and production capacity remain unverified. Push status and Slice 7 results are recorded separately after they occur.
