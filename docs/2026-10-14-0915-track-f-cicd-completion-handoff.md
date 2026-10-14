## Track F — CI/CD Completion (push → test → approve → deploy) Handoff

Status: drafted on 2026-10-14

You are working in /Users/michaelinman/Repos/SchwabUrlShortener-cicd.

Follow these repo standards exactly:

- `AGENTS.md` is binding. Never print, commit, move or delete `.env*`, credentials, `.claude/`, `.codex/`, or the assignment PDF.
- Dated documents only: `docs/YYYY-MM-DD-HHMM-<purpose>.md` (America/Chicago time).
- Real checks only. Do not stub an Azure call, and do not claim a gate is operational until a real run has passed it.
- Ask Michael before every push, before any Azure write (identity, role assignment, federated credential), and before any GitHub settings change. Show the exact command and wait for "approve".
- Never use `--force` on a push. Never weaken or suppress a failing gate to turn CI green.

Task: Finish the CI/CD pipeline so that a push to `main` runs every test gate, and on success deploys the new application images to the existing Azure environment after Michael's approval, then migrates, smoke-tests and rolls back automatically on failure.

Goal:

This pipeline will be evaluated as part of the Schwab assignment. A reviewer should be able to open the repository and see:

1. Every PR and every push to `main` runs build, vulnerability, test, container-scan, secret-scan, CodeQL and Bicep gates.
2. A push to `main` that passes CI triggers a deployment that pauses for Michael's approval in the `production` environment.
3. The deployment authenticates to Azure through OIDC with a narrowly scoped identity, and stores no Azure credentials or app secrets in GitHub.
4. It deploys images by digest, runs the migration job, verifies health and a real guest flow, and restores the previous images if the smoke test fails.

Important context:

- **`origin/main` is now `ee5f0b2`** (Track D hardening; it includes the vulnerable-package fix `514bf64` and the favicon fix `8ebdb18`). Your branch `codex/cicd` (`403910c`) predates it, which is why the vulnerability job was red. Rebase first.
- **Azure is live** in `rg-schwab-url-shortener` (centralus, subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, account `social@mediaraq.com`). It serves `https://ziplio.io`. Container apps: `ca-shortener-api`, `ca-shortener-web`, `ca-shortener-analytics`, plus a Redis app and a migration job. Discover the exact names; do not assume them. The resource group has a `CanNotDelete` lock, which permits updates and creates.
- **Track C's infrastructure is still on `codex/azure-infra`** (`d03ee13`, worktree `/Users/michaelinman/Repos/SchwabUrlShortener-infra`) and is not on `main`. `infra/main.bicep` takes `@secure()` parameters (`postgresPassword`, `redisPassword`, `webClientSecret`), and `azure.yaml` has a `postprovision` hook that configures Entra through Microsoft Graph. The routine pipeline must therefore **not** run `azd provision`; see the design decision below.
- **Images:** azd cannot select a Dockerfile target. Track C builds with `az acr build --target <api|web|analytics>` (see `infra/scripts/build-images.sh`) and deploys by digest.
- **The repository is currently private.** On Michael's GitHub plan, environment required reviewers and branch protection are only available once it is public. Michael will make it public after a history clean-up. Prepare every setting as a reviewed `gh api` script and run it only after he confirms the repository is public.
- Your existing work to keep and build on: `ci.yml` (build `-warnaserror` with `NuGetAudit=false`, a separate vulnerability gate, Compose integration, host startup checks, Bicep lint/skip), `deploy.yml` (disabled behind `DEPLOY_ENABLED`), `.github/scripts/*`, `dependabot.yml`, and the runbook in `docs/runbooks/`.

**Design decision (Michael):** separate application delivery from infrastructure changes.

- **Application CD runs automatically on every push to `main`:** build images, migrate, update the apps by digest, smoke-test, roll back on failure. It needs no app secrets, no Key Vault access and no Graph permissions.
- **Infrastructure changes** (Bicep and Entra) remain operator-applied with `azd provision --preview` (what-if) and then `azd provision` from Michael's machine. CI still validates them (`bicep lint` and `bicep build`). This is a documented trade-off: it keeps the database password, Redis password and client secret out of GitHub, and keeps Graph rights off the pipeline identity.

In scope only:

1. Rebase `codex/cicd` onto `origin/main` (`ee5f0b2`). Resolve conflicts without dropping any track's changes. Confirm the vulnerability gate now passes locally.
2. **Gate deploy on CI.** Change `deploy.yml` to trigger on `workflow_run` of the CI workflow (`types: [completed]`, `branches: [main]`), run only when `github.event.workflow_run.conclusion == 'success'` and the event was a push, check out `github.event.workflow_run.head_sha`, and keep `workflow_dispatch` (main only) for manual redeploys. Keep `concurrency: production-deploy` with `cancel-in-progress: false`, and `permissions: {}` at the top level.
3. **Rewrite the deploy job for app-only delivery** in the `production` environment, using `id-token: write` and `contents: read`:
   1. Log in with `azure/login` (OIDC).
   2. Record the currently running image of each app (for rollback) in the step summary.
   3. Build the three images in ACR with `--target`, tagged with the commit SHA, and resolve each **digest**. Reuse `build-images.sh` if it can run standalone; otherwise call `az acr build` directly.
   4. Run the migration job with the new API image digest, and wait for it to succeed. Migrations must stay backward-compatible (expand, then contract). Say so in the runbook.
   5. Update API, then Analytics, then Web with `az containerapp update --image <registry>@sha256:…`, and wait for each new revision to be healthy.
   6. Smoke-test against `PUBLIC_ORIGIN`: `/health/live` 200; `/health/ready` 200; guest create returns 201; `/r/<code>` returns 302 to the target; `https://www.<domain>/x` redirects 301 to the apex; `/favicon.ico` 200. Extend `smoke-deploy.py` and keep it standard-library only.
   7. On any failure after step 5, restore the recorded previous images and fail the job. A migration is not rolled back automatically; document that.
   8. Write a step summary: commit, digests, revisions, smoke results, rollback status.
4. **Least-privilege deploy identity** (propose the commands, then run only after Michael approves): a user-assigned managed identity `id-github-deploy-ziplio` in the resource group, with a federated credential for subject `repo:mike-inman/SchwabUrlShortener:environment:production`. Role assignments at the narrowest workable scope: `AcrPush` on the registry; `Container Apps Contributor` (or the narrowest built-in role that can update apps and start jobs) on the resource group; `Reader` on the resource group. No Key Vault, Graph, Owner or User Access Administrator rights. Record the identity's client ID in the runbook (it is not a secret).
5. **Add CI security gates:**
   - CodeQL (C#, `security-and-quality`) on PRs, pushes to `main`, and a weekly schedule.
   - `actions/dependency-review-action` on PRs (fail on High).
   - gitleaks over the full history, failing on findings. Add a `.gitleaks.toml` only for proven false positives, each one commented.
   - Trivy image scans of the three built images in CI (fail on fixable HIGH and CRITICAL; upload SARIF).
   - Extend Dependabot to cover `github-actions` and `docker` as well as `nuget`.
   Pin every action by full commit SHA with a version comment, as you already do.
6. **GitHub settings script** `.github/scripts/configure-repo-protection.sh`, documented in `docs/runbooks/` (run only after Michael confirms the repository is public, and after he approves):
   - A `production` environment with Michael (`mike-inman`) as the required reviewer, deployment branch policy limited to `main`, and `prevent_self_review` off (he is the only reviewer).
   - Repository variables: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, `ACR_NAME`, `PUBLIC_ORIGIN=https://ziplio.io`, then `DEPLOY_ENABLED=true` last.
   - Branch protection (or a ruleset) on `main`: PRs required, required status checks (the job names from item 1 and item 5), linear history, no force pushes, no deletions.
   - Secret scanning and push protection on.
   - Delete the GitHub secrets that are no longer used: `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `WEB_CLIENT_SECRET`.
7. Add a CI status badge and a "Deployment pipeline" section to the README: a short flow diagram, the gates, how approval works, and the rollback behavior.

Out of scope:

- Changing application source, tests, Dockerfiles or Compose files. If a gate exposes a real defect, stop and report it with evidence.
- Changing Bicep or `azure.yaml`, and running `azd provision` from the pipeline. Report infrastructure needs to Track C through Michael.
- Making the repository public, deleting branches, or rewriting history. Those are Michael's submission clean-up.
- Any Entra or Microsoft Graph change.
- Paid GitHub features.

Required discovery before edits:

- `git fetch origin && git log --oneline origin/main -5`: confirm `ee5f0b2` is the head.
- In the infra worktree: `infra/p2-apps.bicep` and `infra/scripts/build-images.sh`. Find the real app names, the migration job name and command, the registry name, and how images are tagged and passed.
- `az containerapp list -g rg-schwab-url-shortener -o table`, `az containerapp job list -g rg-schwab-url-shortener -o table`, `az acr list -g rg-schwab-url-shortener -o table`: read-only.
- `az role definition list --name "Container Apps Contributor"`: confirm it covers `containerApps/write` and `jobs/start`. If it does not, propose the narrowest alternative with evidence.
- `gh api repos/mike-inman/SchwabUrlShortener --jq .visibility` and `gh api repos/mike-inman/SchwabUrlShortener/environments`.
- The job names in your current `ci.yml`, so that the required-check list matches exactly.

Implementation requirements:

- The workflows must pass `actionlint`. Use `set -euo pipefail` in every shell step. Never echo a token or a connection string.
- Image references are always `registry/repo@sha256:…` in the deploy step. Tags are for humans only.
- Each app update waits for its new revision to reach `Running` / healthy with a bounded timeout (≤ 10 minutes) before moving to the next.
- The rollback step runs `if: failure()` only when the update step had started, and it restores exactly the recorded previous digests.
- The deploy job never runs for a `pull_request` or a fork, and never on a branch other than `main`.

Exception / observability requirements:

- Every failure path writes a clear one-line reason to `$GITHUB_STEP_SUMMARY` (which gate failed, which app, which smoke check).
- If the migration job fails, print its execution name and the `az containerapp job execution show` status, not its logs, because logs can contain connection details.
- Telemetry is already in place (Track D, Application Insights). Add the deployed commit SHA as an annotation in the step summary. Do not add new telemetry code.

Realtime / no-polling requirements:

- Not applicable to the application. In the workflow, waiting for a revision or job uses the Azure CLI's status with a bounded loop and backoff of at least 10 seconds. That is acceptable for CI, but keep it bounded and print progress at most once every 30 seconds.

Testing:

- Run `actionlint` on all workflows, and `bash -n` and `shellcheck` (if available) on the scripts. Run `python3 -m py_compile` on the Python scripts.
- Run the full CI on a PR from the rebased branch. Every job must be green, and attach the run URL.
- Run a dry run of the deploy steps locally against Azure in **read-only** mode: resolve the current digests, run what the rollback step would record, and run the smoke script against `https://ziplio.io`.
- After Michael approves enabling it: one real deployment through the `production` approval. It must show the approval pause, the digests, the migration success, the smoke pass and the step summary. Then trigger a deliberate smoke failure on a **manual dispatch** run (for example, a temporary `SMOKE_EXPECT_FAIL=true` input) to prove the rollback restores the previous digests. Remove the input afterwards, or document it as a test-only switch.

Documentation:

- `docs/2026-10-14-HHMM-track-f-cicd-completion-validation.md`: the run URLs, the screenshots or links showing the approval gate, the digests deployed, the rollback proof, and what remains unverified.
- Update `docs/runbooks/` with the final setup sequence, the identity's role assignments and the reason for each, how to approve a deployment, how to roll back manually, and the decision to keep infrastructure out of the pipeline.

Completion criteria:

- [ ] `codex/cicd` is rebased on `ee5f0b2`, and CI is fully green on a PR, with a run link recorded.
- [ ] CodeQL, dependency review, gitleaks, Trivy and Dependabot (nuget, actions, docker) are active, and their results are visible in the run.
- [ ] `deploy.yml` triggers only after CI succeeds on `main`, pauses for approval in `production`, and deploys by digest.
- [ ] The OIDC identity exists with only the approved roles, and no Azure credential or app secret is stored in GitHub.
- [ ] One approved production deployment succeeded end to end (migrate, update, smoke), with its link recorded.
- [ ] The rollback is proven once on a manual run.
- [ ] Branch protection, environment reviewer, secret scanning and push protection are applied (after the repository is made public), and the script used is committed.
- [ ] README badge and pipeline section are added; validation record and runbook are committed; Michael has approved each push.
