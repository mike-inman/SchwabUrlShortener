# Track G post-rebase CI validation

Date: 2026-10-14 11:13 CDT

Worktree: `/Users/michaelinman/Repos/SchwabUrlShortener-insights`

Branch: `claude/link-insights-collection`

## Purpose and scope

Rebase Track G onto PR #2's CI/CD merge at `7493c2e`, retain both tracks' changes, run the complete disposable local suite, and submit Track G through the PR CI gates. This record supplements the earlier Track G implementation plan and validation record.

## Work and local evidence

- Rebasing `974e17d` and its five preceding branch commits onto `7493c2e` produced documentation conflicts in `README.md` and the well-architected review. The resolution keeps the CI badge, Track F status, and Track G status. The rebased implementation commit is `1d5c818`.
- The merged Track F host-startup probe clears inherited environment variables. Track G requires `VisitorHash__DailySecret` at API runtime, so `.github/scripts/check-host-startup.sh` now supplies an explicitly disposable placeholder for that API probe. The production requirement is unchanged.
- Started a fresh Compose project, `schwab-trackg-rebase-20261014`, using the temporary Track G override on inspected subnet `10.251.48.0/24` and loopback port 18095. The previous `schwab-trackg-check` project was brought down without deleting its volumes to release the subnet; no other Compose project was changed.
- PostgreSQL, Redis, Kafka, API, Web, Analytics, and proxy reached healthy state. `http://localhost:18095/` returned 200. The disposable database is `Schwab_test_local`.
- Full Compose solution tests passed twice on the rebased implementation: 39 integration, 43 unit, and 69 Web tests (151 total), with no failures or skips. The second run followed the startup-probe adjustment.
- CI's API, Web, and Analytics host-startup probe passed after that adjustment. A Release solution build with warnings as errors passed with 0 warnings and 0 errors. `git diff --check` passed.

## Handoff and remaining gates

Michael reported that Track C deployed the `visitor-hash-secret` Key Vault secret and the API's `VisitorHash__DailySecret` reference, with healthy revision `ca-shortener-api--0000006`. This is operator confirmation, not an independent Azure check from this validation run.

Next: push only this branch, open a PR to `main`, and require the full CI gate set, including build, integration, vulnerabilities, gitleaks, and Trivy. Report the run link and request Michael's approval before a rebase-merge. No direct push to `main` is authorized. Signed-in browser and live Azure ingestion acceptance remain separate from this local suite.
