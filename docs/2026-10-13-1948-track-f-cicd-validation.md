# Track F CI/CD validation — 2026-10-13 19:48 CDT

## Purpose, scope, and baseline

Implement the Track F handoff in `/Users/michaelinman/Repos/SchwabUrlShortener-cicd` on `codex/cicd`, preserving other worktrees and credentials. The requested handoff, current Azure contract, and Well-Architected review were copied from the main checkout and committed first as `3360e5d`. `origin/main` then advanced to `912ac3b` with the required committed `LiveUpdates` defaults; Track F merged that upstream commit as `00a275d` before writing CI. No Track F source, test, infrastructure, Compose, Dockerfile, or credential file was edited.

## Workflow design

- `ci.yml`: pull requests and pushes to `main`; zero-warning .NET build, unit and non-Redis Web tests with TRX artifact, fresh placeholder-only Compose integration suite, real host startup from committed `appsettings.json` and an explicit infrastructure/identity allowlist, High/Critical NuGet audit, and Bicep build/lint when Track C's file is merged. Actions are pinned to resolved full commit SHAs. Each job has `contents: read`; no CI job has Azure credentials.
- `deploy.yml`: push to `main` or manual dispatch, skipped unless `DEPLOY_ENABLED=true`, `production` environment gate, OIDC Azure CLI and `azd` sign-in, resource-ID-only what-if summary before provisioning, image-by-digest `azd` deploy, migration execution and status check, then public guest smoke test. It refuses the current P1-only template. GitHub reviewer, variables, secure parameters, and federated identity are operator steps in [the runbook](runbooks/ci-cd.md).
- Dependabot: weekly grouped NuGet, GitHub Actions, and Docker base-image updates.

## Local evidence

- `actionlint v1.7.7` passed on both workflows.
- `bash -n`, `sh -n`, Python compilation, and `git diff --check` passed on the scripts and current diff.
- A synthetic what-if JSON smoke check confirmed the summary includes only the change type and resource ID, omitting a deliberately supplied resource property. Azure what-if output shape remains unverified until a gated P2 run.
- `dotnet build SchwabUrlShortener.slnx --nologo -m:1 /nodeReuse:false -warnaserror` passed with zero warnings and errors on .NET SDK 10.0.107.
- Local unit tests passed 30/30. Local Web tests excluding `LiveUpdateRedisTests` passed 58/58.
- The Compose test configuration passed `docker compose config --quiet`. Its first start could not allocate a network because this host's automatic address pools are exhausted. A restart using the documented `deploy/network.override.example.yaml` subnet, checked unused on this host, created a fresh `schwab-trackf-local` project. The full solution suite passed **116/116** against its separate migrated `Schwab_test_local` database and real Redis/Kafka: integration 27/27, unit 30/30, Web 59/59.
- The first startup-check run passed API and Web. Analytics bound its built-in default port, so its probe could not reach it; the script now supplies `ASPNETCORE_HTTP_PORTS` for that host. A full rerun passed API, Web, and Analytics. The temporary Compose project and its named volumes were removed after validation; existing Schwab and unrelated projects were not touched.
- The High/Critical vulnerability gate **fails** against the current checked-out dependency graph: transitive `System.Security.Cryptography.Pkcs` and `System.Security.Cryptography.Xml` version `10.0.10` have four distinct High advisories across 10 project/framework occurrences. Track F cannot update package references under its file fence. CI must remain red until the owning source track updates and verifies the affected packages; the workflow does not suppress advisories.

## Pending gates and operator work

- Michael approved publication on 2026-10-14. `codex/cicd` was pushed at `e19de0a` and [draft PR #2](https://github.com/mike-inman/SchwabUrlShortener/pull/2) was opened against `main`.
- GitHub reports this repository as **private** with zero configured environments at inspection. Required-reviewer protection must be confirmed available under Michael's GitHub plan before enabling `DEPLOY_ENABLED`; an unprotected auto-created `production` environment is not an approval gate.
- Track C's current checkout has P1 foundation resources only. It has no P2 Container Apps or migration job, and `infra/main.bicep` accepts only `deploymentPhase=p1`. The deploy job is intentionally disabled and cannot be called operationally validated. Its what-if parsing, production identity permissions, image hook, migration, and public smoke path await P2 and a gated run.
- Michael must configure the `production` environment reviewer, the dedicated federated deployment identity, repository variables, environment secrets only as required by the final Bicep, and finally `DEPLOY_ENABLED`; exact commands and sequence are in the runbook.
- README badge was left for after a published CI run because the explicit Track F edit fence excludes README and no run URL exists yet.

## Outcome

Track F files are published for draft review. CI is not yet green because of the verified dependency advisories; deployment and operational acceptance are pending.

## Remote CI — 2026-10-14 07:23 CDT

The [first PR CI run](https://github.com/mike-inman/SchwabUrlShortener/actions/runs/38562714387) completed with failure. Its disposable Compose integration job succeeded: integration 27/27, unit 30/30, Web 59/59, then API/Web/Analytics startup passed. Bicep succeeded as an expected skip because Track C's Bicep has not merged. The dedicated vulnerability job failed on the same four High advisories found locally.

The build job failed before its unit/Web steps because `-warnaserror` promoted NuGet's advisory warnings, including Moderate, into errors. Track F changed that job's restore/build commands to set `NuGetAudit=false` there; the separate required vulnerability job still audits and fails on High/Critical findings. The revised command passed a local Release build with zero warnings, and `actionlint` passed.

The [second PR CI run](https://github.com/mike-inman/SchwabUrlShortener/actions/runs/38563437370) on `eeffb73` completed. Build passed with zero warnings; unit 30/30 and non-Redis Web 58/58 passed, and both TRX results were uploaded as `unit-web-trx`. The separate Compose job passed integration 27/27, unit 30/30, and Web 59/59 against real disposable services; API, Web, and Analytics startup checks all passed. Bicep remained an expected skip. The overall run is red **only** because the dedicated vulnerability job found the same four High advisories. No Azure deployment was attempted.
