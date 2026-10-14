# Track D telemetry sampling validation — 2026-10-14 13:34 CDT

## Purpose and approved scope

Set `OpenTelemetry__SamplingRatio=0.25` on the existing API, Analytics, and Web Container Apps without changing their image digests or any other app configuration. Michael approved the three-app focused what-if and live application after the `codex/azure-infra` history rewrite. Before any other action in this turn, the worktree ran `git fetch origin && git reset --hard origin/codex/azure-infra` and landed on rewritten remote head `f15e032c826bdc8da2f908d5cc166c243f647517`; no old local history will be pushed. Michael's exact history-grep command returned `0`; the address is not copied into this note.

Operator `social@mediaraq.com` and subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e` matched the approved account. The resource-group lock `lock-shortener-cannot-delete` was `CanNotDelete` and will not be removed.

## Pre-update references and preview

| App | Current image digest | Current revision | Sampling override |
| --- | --- | --- | --- |
| API | `sha256:7cd47ea94b5f6fca21eead3400c377de69ad669a9935f529acda1e664f069faf` | `ca-shortener-api--0000007` | Missing |
| Analytics | `sha256:453880bb9290b7a32adfacc26f6c4035490c0af222be2ce3c4a930dcc4da2c44` | `ca-shortener-analytics--0000001` | Missing |
| Web | `sha256:8d7e056e507c8a72b16101743e6a8c08bcf9f2a0732e0e67ecc65eece85c0a01` | `ca-shortener-web--0000007` | Missing |

`infra/p2-apps.bicep` now declares the env value for each app. Its Bicep build and lint succeeded; existing unrelated linter warnings remain. A refreshed provider what-if built from each live app with only the env addition reported exactly three Modifies (`ca-shortener-api`, `ca-shortener-analytics`, `ca-shortener-web`), each at `properties.template.containers` array granularity; no Create/Delete. The preview inputs preserved each current image. Azure's GET redacts the Analytics `kafka-username` literal, so the preview restored the existing noncredential Bicep literal `$ConnectionString`; the provider reported no secret delta.

## Execution and outcome

API was updated with only `--set-env-vars OpenTelemetry__SamplingRatio=0.25`. New revision `ca-shortener-api--0000008` retained the planned API image, reported the exact env value, reached `Running/Healthy`, and public `/health/ready` returned 200. No rollback was needed.

Analytics was updated next with the same single env addition. New revision `ca-shortener-analytics--0000002` retained its planned image, reported `0.25`, and reached `Running/Healthy`. No rollback was needed.

Web was updated last with the same single env addition. New revision `ca-shortener-web--0000008` retained its planned image, reported `0.25`, reached `Running/Healthy`, and public `/` returned 200. No rollback was needed.

Read-only public smoke then passed: liveness 200, readiness 200, `/favicon.ico` 200, and www-to-apex 301. The Web root returned 200 with a CSP containing `script-src` and a nonce; the same-origin ApexCharts module returned 200. This follow-up did not create another guest link.

Final read-only checks confirmed API `ca-shortener-api--0000008`, Analytics `ca-shortener-analytics--0000002`, and Web `ca-shortener-web--0000008` all `Running/Healthy` with `OpenTelemetry__SamplingRatio=0.25` and their original image digests. All three still have an Application Insights connection setting without printing its value; API and Web retain `PublicLinks__BaseUrl=https://ziplio.io/`. The group lock remains `CanNotDelete` and was never removed. If any app stage had failed, the focused rollback would have removed that app's sampling override to restore its preceding configuration; no rollback was needed.

The customer contract has no telemetry-settings table, so no contract row was changed. The Bicep edit and this note are the only intended repository changes. PR #3 was rebase-merged while this change was being recorded. This sampling change is proposed separately from the merged infrastructure PR and is not merged here.
