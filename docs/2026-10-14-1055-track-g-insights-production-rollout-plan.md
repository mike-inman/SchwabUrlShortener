# Track G Insights production rollout plan — 2026-10-14 10:55 CDT

## Purpose and current gate

Deploy the merged Track G application and the already merged Track D hardening from one immutable `main` commit, after a provider-backed what-if and Michael's approval. Track D's rate limits, security headers/CSP, redirect, and observability code are in the current repository `main` but are **not yet live**. Track G is currently only on `claude/link-insights-collection`: remote `main` is `7493c2ef26f6d46409a743d7e179738c2edf14d6`, while that branch is `75426923e58a52f73312269117d7ffbd676826cd`. Therefore the exact image digests and deployable what-if cannot be produced yet. Do not build or deploy from the Track G branch or use placeholder digests.

The Key Vault `visitor-hash-secret`, its one-secret API identity grant, and the API's reference are already deployed; API revision `ca-shortener-api--0000006` is healthy on the previous image. See the [dated secret rollout evidence](2026-10-14-1017-track-g-visitor-hash-secret-rollout-plan.md). Preserve all existing secrets, customer identity, custom-domain bindings, data, and unrelated Azure resources.

## Scope and lock handling

Target only subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, resource group `rg-schwab-url-shortener`, ACR `acrshortenerf2xpl6`, migration job `caj-shortener-migrate`, and Container Apps `ca-shortener-api`, `ca-shortener-analytics`, and `ca-shortener-web`. Record previous digests and live revision health before changing anything. The group has `lock-shortener-cannot-delete` at `CanNotDelete`.

**No lock removal is planned or approved for this rollout.** Updating images and starting the existing migration job should not require deletion. If any provider what-if contains Delete, or an operation reports `ScopeLocked`, stop before that mutation and submit a revised plan and what-if that explicitly names the lock removal, duration, exact target, guaranteed restoration, and verification. Do not remove the lock as an incidental repair.

## Preview before deployment

1. Wait until Track G is on remote `main`; fetch and identify the exact immutable `main` SHA. Verify that SHA includes Track G and Track D, the worktree is clean, and the source contains the `www` → apex redirect, Web `/favicon.ico`, and security-header middleware. Recheck Azure account, lock, API secret reference, and current images.
2. Build API, Analytics, and Web images from **that exact `main` commit** using `infra/scripts/build-images.sh` with `IMAGE_SOURCE_REF=<main SHA>`. It archives only committed `Dockerfile` and `src` into a temporary build context and resolves each ACR image by digest. Record the SHA and three digest references; never use mutable tags for deployment.
3. Construct narrow provider what-ifs from each live resource, changing only the image field: first the migration job to the new API digest, then API, Analytics, and Web in that order. Confirm the job preview shows only one job Modify and each app preview shows only its named app Modify, with no Create/Delete, identity, secret, ingress, route, or scale changes. Capture provider normalization noise explicitly. Show Michael the exact four previews and digest table **before any migration job or app image update**; wait for his rollout approval.

## Approved execution order after the preview gate

1. Update the existing migration job to the new API digest. Start one manual execution and require its terminal `Succeeded` state and successful migration logs, without printing credentials. Do not update the serving API until this succeeds. If it fails, leave serving images unchanged and report the failure.
2. Update API to its new digest; require its latest revision `Healthy` and `Running`, `https://ziplio.io/health/ready` 200, and the existing visitor-hash Key Vault reference. Then update Analytics to its new digest and require readiness/consumer health. Then update Web to its new digest and require `Healthy` and `Running`.
3. Smoke-test both HTTPS hosts and the exact requested paths: apex `/` and `/health/ready`; anonymous guest create and `https://ziplio.io/r/<code>` 302 to the test destination; `https://www.ziplio.io/r/<code>?probe=1` 301 to `https://ziplio.io/r/<code>?probe=1`; `https://ziplio.io/favicon.ico` 200; and response headers including CSP, `X-Content-Type-Options`, `Referrer-Policy`, and `Permissions-Policy` where configured. Verify the Web page and hosted chart asset load without loosening CSP.
4. Inspect click-consumed, deduplicated, outbox-age, and consumer-lag signals plus application errors. In a signed-in customer workspace, load one managed link's Insights page and confirm its tiles/charts or explicit empty state, authorization boundary, and live update path. A public smoke test does not establish signed-in Insights acceptance.
5. Record deployment names, SHA, digests, revision states, smoke results, observed telemetry, any rollback, and unresolved checks in a dated validation note. If a stage fails, stop before later image updates; use the recorded previous digest for a focused rollback preview and request approval if the rollback changes scope.

## Acceptance criteria and current outcome

- [ ] Track G is in remote `main`; exact source SHA and three digests recorded.
- [ ] Four focused provider what-ifs reviewed and approved before migration or image deployment.
- [ ] Migration execution succeeds before new API image serves traffic.
- [ ] API, Analytics, and Web become healthy in that order.
- [ ] Requested HTTPS, guest, redirect, favicon, headers/CSP, and signed-in Insights checks pass or are recorded with exact blockers.
- [ ] `CanNotDelete` lock remains present throughout; no deletion or unrelated setting change occurs.

Current status: **plan prepared; exact what-if pending Track G's merge to `main` and the resulting immutable image digests. No rollout deployment started.**
