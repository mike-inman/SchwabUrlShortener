## Track F — CI/CD Handoff
Status: drafted on 2026-10-13 19:35 CDT. Can start now; the deploy job is enabled only after Track C's environment exists.
You are working in /Users/michaelinman/Repos/SchwabUrlShortener-cicd.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Create the worktree from the main checkout: `git -C /Users/michaelinman/Repos/SchwabUrlShortener fetch origin && git -C /Users/michaelinman/Repos/SchwabUrlShortener worktree add /Users/michaelinman/Repos/SchwabUrlShortener-cicd -b codex/cicd origin/main`.
- Copy this handoff, `docs/2026-10-13-1720-azure-scaleout-contract.md`, and `docs/2026-10-13-1930-well-architected-review.md` from `/Users/michaelinman/Repos/SchwabUrlShortener/docs/` into the worktree's `docs/` and commit them first.
- Edit only `.github/**`, a new `docs/runbooks/ci-cd.md`, and your dated docs. Never edit `src/`, `tests/`, `infra/**`, `Dockerfile`, or Compose files; if the pipeline needs a change there, report it.
- Preserve all other worktrees. Do not modify `.env*`, credentials, local agent configuration, or the assignment PDF. Never commit or print a secret.

Task: Add GitHub Actions continuous integration and a gated, secretless deployment to the Azure environment, plus Dependabot, for `mike-inman/SchwabUrlShortener`.

Goal:
Every pull request to `main` is built, tested, scanned, and linted automatically. A merge to `main` can deploy to `rg-schwab-url-shortener` through `azd`, authenticated with GitHub OIDC workload identity federation (no Azure secret stored in GitHub), behind a manual approval. Dependencies are kept current by Dependabot.

Important context:
- The repo has no `.github/workflows`. Remote: `https://github.com/mike-inman/SchwabUrlShortener`.
- Build: `dotnet build SchwabUrlShortener.slnx`. The integration suite requires the Compose test profile (`compose.yaml`, `compose.local.yaml`, `compose.test.yaml`, `--profile test`, `deploy/local.env.example`); see the Slice 6 and Slice 7 records for the exact commands. The real-Redis Web test also needs that profile.
- Infrastructure: Track C's `infra/main.bicep`, `azure.yaml`, and `infra/scripts/build-images.sh` (remote ACR builds by digest). The `azd` environment is `prod`.
- Track C's managed identities and Key Vault already hold all runtime secrets; the pipeline needs only Azure deployment rights.

In scope only:
1. `.github/workflows/ci.yml`, on pull requests to `main` and pushes to `main`:
   - Restore, build with zero warnings, unit and Web tests (excluding the real-Redis test), with results uploaded as artifacts.
   - Integration job: run the Compose test profile exactly as the Slice 7 record does, on an `ubuntu-latest` runner, and fail on any test failure. Use only `deploy/local.env.example` placeholder values.
   - `dotnet list package --vulnerable --include-transitive`, failing on any High or Critical finding.
   - `az bicep build` and `az bicep lint` on `infra/main.bicep`, once `infra/` exists on `main`.
   - Pin every third-party action to a full commit SHA, set `permissions:` to the minimum per job (`contents: read` by default), and set concurrency to cancel superseded runs on the same branch.
2. `.github/workflows/deploy.yml`, on push to `main` and manual dispatch:
   - GitHub environment `production` with required reviewer Michael (configured by Michael in repository settings; document the exact steps).
   - `azure/login` with OIDC (`id-token: write`), using repository variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` (`eea269ab-1571-48a5-a18c-824ba5859cd5`), `AZURE_SUBSCRIPTION_ID` (`baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`). No client secret.
   - `azd` with the `prod` environment: build images by digest, `azd provision` with what-if output written to the job summary first, then `azd deploy`, then run the migration job, then smoke-test `/health/ready`, guest create, and redirect against the public host.
   - Secure `azd` inputs (`POSTGRES_PASSWORD`, `WEB_CLIENT_SECRET` if still needed) come from GitHub environment secrets set by Michael; document that and never echo them. If Track C's Bicep can run without re-supplying them on update, prefer that and record it.
   - Disabled by default (`if: vars.DEPLOY_ENABLED == 'true'`) until Track C's environment is live.
3. Deployment identity: give Michael the exact commands to create a dedicated Entra application `github-schwab-url-shortener-deploy` with a federated credential for `repo:mike-inman/SchwabUrlShortener:environment:production`, and to assign it `Contributor` plus `User Access Administrator` limited to `rg-schwab-url-shortener` only (or the narrowest roles Track C's Bicep needs; determine and record them). Do not run them yourself.
4. `.github/dependabot.yml`: weekly updates for NuGet, GitHub Actions, and Docker base images, grouped to keep pull-request volume low.
5. `docs/runbooks/ci-cd.md`: what each workflow does, how to enable deployment, how to approve, how to roll back (re-run deploy for a previous commit, which redeploys its image digests), and how to rotate the federated credential.

Out of scope:
- Any change outside `.github/**` and the runbook.
- Running deployments, creating Entra applications, or changing GitHub repository settings yourself.
- Paid tooling.

Required discovery before edits:
- Read the contract, the review, `README.md`, `compose.test.yaml`, and the Slice 6 and Slice 7 records for test commands.
- Check current documentation, recording URLs, for `azure/login` OIDC, `azd` in GitHub Actions with federated credentials, and GitHub environment protection rules.
- Confirm the action versions and their commit SHAs.

Implementation requirements:
- Least privilege for workflow tokens and the Azure deployment identity.
- No secret printed; mask any derived value.
- CI passes on the current `main` before it is merged; if an existing test is flaky in CI, report it rather than skip it.

Testing:
- Validate workflow syntax with `actionlint` if available.
- Push the branch and open a draft pull request only with Michael's approval in chat; confirm CI runs and passes on it. Record the run URL.
- The deploy workflow stays disabled; validate it with a dry run of its non-Azure steps if possible, and record what could not be exercised.

Documentation:
- Add `docs/2026-10-13-<HHMM>-track-f-cicd-validation.md` with workflow summaries, run URLs, and what remains for Michael (repository variables, environment reviewer, deployment identity commands, `DEPLOY_ENABLED`).
- Update the review file's status for O1 and O3, and README surgically with build badges.

Completion criteria:
- [ ] CI runs on pull requests and pushes: build, unit, Web, integration, vulnerability scan, Bicep lint; actions pinned by SHA; minimal permissions.
- [ ] Deploy workflow uses OIDC with no stored Azure secret, a manual approval environment, what-if before provisioning, migration, and smoke tests; disabled until enabled.
- [ ] Deployment identity commands scoped to the resource group, ready for Michael.
- [ ] Dependabot configured.
- [ ] Runbook written; only `.github/**`, the runbook, and dated docs changed.

Report back: branch commit, CI run URL, and the exact steps Michael must take to enable deployment.
