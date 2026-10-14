# Production revision health wait incident — 2026-10-14 16:36 CDT

## Purpose and scope

Record deploy run [38626309390](https://github.com/mike-inman/SchwabUrlShortener/actions/runs/38626309390), repair its revision health parser, and require a gated manual dispatch proof before another automatic deployment. This change affects only the revision wait and its CI regression test. It does not change application images, traffic, credentials, or the resource lock.

## Timeline (UTC, 2026-10-14)

- 19:45:14: GitHub created the production deploy run for commit `b4ee0efef87003e1533e4a0355eff85efd461dd5`.
- 20:02:19–20:05:39: preflight, Azure OIDC login, previous-image capture, release-image build, and migration step succeeded.
- 20:05:39–20:17:21: the API update wait repeatedly printed `Running/pending` for revision `ca-shortener-api--0000009` and timed out after its ten-minute health window.
- 20:17:21–20:55:59: rollback updated the previous digests but its waits likewise printed `Running/pending` for API, Analytics, and Web and eventually failed. The public smoke step was skipped.
- Afterward: Azure showed API revision `ca-shortener-api--0000010` Running/Healthy with 100% traffic, and Analytics `--0000002` and Web `--0000008` Running/Healthy on their previous image digests. Queued run [38629458408](https://github.com/mike-inman/SchwabUrlShortener/actions/runs/38629458408) was cancelled.

## Impact and root cause

No user impact was observed. Application Insights request telemetry for 20:00–21:00Z showed Web 160, API 193, and Analytics 111 requests, with 0 server errors (5xx). The deployment and rollback jobs were falsely marked failed; application rollout and smoke validation did not complete. Azure's `az containerapp revision show` JSON places the statuses at `properties.runningState` and `properties.healthState`. For the serving revisions, the values are `Running` and `Healthy`. The old `revision list --query ... --output tsv` command emitted those values on separate lines. Bash `read -r state health <<< "$row"` consumed only the first line, leaving `health` empty. The wait therefore logged `Running/pending` until timeout even for healthy revisions.

## Fix and verification

The wait now calls `az containerapp revision show --output json` and parses both properties into one tab-separated record with `jq`, requiring the returned revision name to match. Missing status remains pending, and an actual `Failed` or `Unhealthy` state still fails. The same wait function serves updates and rollback.

The parser test uses sanitized JSON projections captured from `az containerapp revision show` for Web `--0000008`, Analytics `--0000002`, and API `--0000009` and `--0000010`. The earlier API revision is now `Stopped/Healthy`; the test keeps that combination distinct from `Running/Healthy`. The deployment identity has `Reader` on the resource group, whose built-in actions include `*/read`; the failed run also read revision status. Direct proof that this identity can use the new `revision show` path awaits the gated manual dispatch.

Local parser test and Bash syntax check passed. `DEPLOY_ENABLED` was set to `false` and read back from GitHub to prevent an automatic deployment while the fix is prepared. Before any other deployment: push the reviewed fix after Michael's approval, get all required CI checks green, merge only with Michael's approval, manually dispatch `Deploy production` from the fixed `main` with `smoke_expect_fail=false`, and wait for Michael to approve the `production` environment. Confirm the run passes revision waits and public smoke checks, then verify the final Azure revision and traffic state. Do not approve the environment on Michael's behalf.
