# Track F CI/CD completion validation — 2026-10-14 09:34 CDT

## Scope and baseline

Worktree `/Users/michaelinman/Repos/SchwabUrlShortener-cicd`, branch `codex/cicd`. Rebased onto `origin/main` at `ee5f0b2`; the conflict in the Well-Architected review retained Track D's newer status and Track F's historical local-validation note. The main checkout's untracked handoff was copied into this worktree without moving or deleting it. No application source, tests, Dockerfiles, Compose files, Bicep, `azure.yaml`, credential file, or other worktree was edited.

The prior green four-job PR run [38567078514](https://github.com/mike-inman/SchwabUrlShortener/actions/runs/38567078514) belongs to commit `403910c` **before** this implementation. It is not evidence for the new CodeQL, dependency review, gitleaks, Trivy, or deployment design. A new PR run link remains pending push approval.

## Implemented locally

- CI adds CodeQL, PR dependency review, full-history gitleaks, and API/Web/Analytics Trivy image scans with SARIF upload; the existing build, integration, vulnerability, and conditional Bicep jobs remain. Dependabot already covers NuGet, Actions, and Docker.
- Application delivery now follows only successful `main` push CI or a `main` manual dispatch. It checks out the CI commit, rejects a stale `main` SHA, waits in `production`, uses OIDC, records previous digests, builds Dockerfile targets in ACR, migrates, updates API/Analytics/Web by digest with bounded health checks, runs public smoke checks, and restores previous images after update or smoke failure. The migration schema is not automatically reversed.
- Added a reviewed GitHub settings script and least-privilege Azure identity commands to the runbook. None were executed. The README documents the pipeline and badge.

## Verification performed

- `actionlint` passed for both workflows with version 1.7.7. `bash -n` passed for the shell scripts; Python compilation and `git diff --check` passed. `shellcheck` was unavailable locally.
- The direct/transitive NuGet check reported **0 unique High/Critical advisories** on the rebased branch.
- Local full-history gitleaks 8.30.1 reported **83 findings** in historical commits, including `.env` paths. The scan output was redacted and is not copied here. No exception was added, and no history or credential file was changed. Michael's separate history cleanup is a prerequisite for a green full-history gate.
- Local Trivy 0.76.0 scans of the three built Dockerfile targets found **12 fixable High/Critical findings per image**. They are in `Microsoft.AspNetCore.App.Runtime.linux-x64` and `Microsoft.NETCore.App.Runtime.linux-x64` version `10.0.12` in the current `mcr.microsoft.com/dotnet/aspnet:10.0` runtime layer; the scanner lists `10.0.13` among fixed versions. The handoff excludes Dockerfile changes, so the image gates remain strict and this requires a source-owning follow-up.
- A unique disposable Compose project `schwab-trackf-mv87ferc` used a temporary override on `10.251.47.0/24`; no other project was stopped or changed. The full solution passed: integration **32/32**, unit **30/30**, Web **64/64**. API, Web, and Analytics host startup checks passed. Only that project's containers, network, and named volumes were removed afterward; the temporary override was removed.
- Read-only Azure discovery confirmed `ca-shortener-api`, `ca-shortener-analytics`, `ca-shortener-web`, `caj-shortener-migrate`, and `acrshortenerf2xpl6`. The capture script resolved the existing application and job digest references without an Azure write. Role definitions showed `Container Apps Contributor` lacks job-start rights and `AcrPush` lacks ACR task scheduling; the runbook proposes scoped Jobs and ACR Tasks roles.
- A read-only public smoke run against `https://ziplio.io` passed `/health/live` and `/health/ready` (200) but stopped at `/favicon.ico` (404). Separate read-only request to `https://www.ziplio.io/x` returned 404 instead of the required 301. This is live deployment/configuration evidence, not a local source test; no guest link was created.

## Pending gates and risks

- The repository is private, has no `production` environment, and GitHub branch protection currently returns HTTP 403 on this plan. Michael's publication and approval are required before the reviewed settings script can run. No OIDC identity or roles have been created; no GitHub variables or secrets have been changed.
- The full-history gitleaks and image gates will fail until the historical findings and vulnerable runtime images are resolved by their respective owners. CodeQL upload, dependency review, and SARIF availability in the present private repository are not claimed as verified.
- Track C's `infra/main.bicep` and `azure.yaml` are still outside `main`; Bicep remains an expected skip there. The pipeline intentionally does not run `azd provision`.
- No new PR run, production approval pause, ACR build, migration, image update, smoke pass, or rollback proof exists yet. These are required before operational acceptance. The current public favicon and www responses would fail the expanded smoke gate if unchanged.
