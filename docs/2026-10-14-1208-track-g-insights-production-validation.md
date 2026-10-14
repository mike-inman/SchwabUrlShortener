# Track G Insights production validation — 2026-10-14 12:08 CDT

## Scope and pre-update state

Operator `social@mediaraq.com`, subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, resource group `rg-schwab-url-shortener`. The `lock-shortener-cannot-delete` lock was present at `CanNotDelete` before deployment. No lock removal is approved or planned. Source is exact remote `main` commit `82ec1b889066c2b789a65c4ad883fa7a250b07d1`, including Track G Insights and Track D hardening. ACR builds `cj4` (API), `cj5` (Analytics), and `cj6` (Web) succeeded from that commit.

The four **current live image references were read and recorded before the first migration-job update command**. Each app can be rolled back with one `az containerapp update --image <previous reference>` command; the migration job has its own previous reference.

| Resource | Previous image reference | Planned image reference |
| --- | --- | --- |
| `caj-shortener-migrate` | `acrshortenerf2xpl6.azurecr.io/shortener-api@sha256:08f0601ba54ff85bc058a7c4015b4499f47b21612a177f53f2fd45aedd951a1b` | `acrshortenerf2xpl6.azurecr.io/shortener-api@sha256:7cd47ea94b5f6fca21eead3400c377de69ad669a9935f529acda1e664f069faf` |
| `ca-shortener-api` | `acrshortenerf2xpl6.azurecr.io/shortener-api@sha256:08f0601ba54ff85bc058a7c4015b4499f47b21612a177f53f2fd45aedd951a1b` | `acrshortenerf2xpl6.azurecr.io/shortener-api@sha256:7cd47ea94b5f6fca21eead3400c377de69ad669a9935f529acda1e664f069faf` |
| `ca-shortener-analytics` | `acrshortenerf2xpl6.azurecr.io/shortener-analytics@sha256:18ac56de1b9d9e5805b7194ed3ad9d50dd8ee0ca5812e7c95cd3bb5d31af97a6` | `acrshortenerf2xpl6.azurecr.io/shortener-analytics@sha256:453880bb9290b7a32adfacc26f6c4035490c0af222be2ce3c4a930dcc4da2c44` |
| `ca-shortener-web` | `acrshortenerf2xpl6.azurecr.io/shortener-web@sha256:26f227bad39c21fb51ee2972323fd74dfdc22b68220cd1e5d857c3aa6ecc7326` | `acrshortenerf2xpl6.azurecr.io/shortener-web@sha256:8d7e056e507c8a72b16101743e6a8c08bcf9f2a0732e0e67ecc65eece85c0a01` |

Four focused provider what-ifs each reported one Modify of its named job or app and no Create/Delete. Azure grouped the image delta under `properties.template.containers`. Other resources were `Ignore`. The Analytics preview first failed validation because Azure's GET redacts the existing `kafka-username` literal; repeating it with the Bicep-declared noncredential literal `$ConnectionString` succeeded with only the Analytics app Modify. No live resource changed during preview.

## Execution and checks

The existing migration job image update succeeded, and a readback showed the planned API digest with provisioning state `Succeeded`. Manual execution `caj-shortener-migrate-w90166l` reached terminal `Succeeded`. Its Azure Log Analytics console stream had three lines through 17:10:22 UTC, including one `Applying migration` and one `LinkInsightsCollection` mention. The direct job-log command returned exit 1 after the execution; Log Analytics supplied the migration log evidence. No serving app had been updated before the job succeeded.

API image update then created revision `ca-shortener-api--0000007`. Its image readback matched the planned API digest, the revision reached `Running` and `Healthy`, and `https://ziplio.io/health/ready` returned 200. No rollback was needed.

Analytics image update then created revision `ca-shortener-analytics--0000001`. Its image readback matched the planned Analytics digest and the revision reached `Running` and `Healthy`. No rollback was needed.

Web image update then created revision `ca-shortener-web--0000007`. Its image readback matched the planned Web digest and the revision reached `Running` and `Healthy`. No rollback was needed before public smoke checks.

The public smoke script reported: liveness 200, readiness 200, `/favicon.ico` 200, www-to-apex redirect 301, guest create 201, apex guest URL host correct, and guest `/r/<code>` redirect 302. Separate Web checks returned root 200 with `Content-Security-Policy` present (`script-src` and a nonce) and `/_content/Blazor-ApexCharts/js/apexcharts.esm.js` 200 from `ziplio.io`. No smoke rollback was needed.

A further route check returned 301 with the exact location `https://ziplio.io/r/no-such-code?probe=1` for `https://www.ziplio.io/r/no-such-code?probe=1`. The Web root supplied `Content-Security-Policy`, `X-Content-Type-Options`, `Referrer-Policy`, and `Permissions-Policy` headers. These are live Track D hardening observations.

## Configuration and telemetry

Read-only Container Apps checks found `APPLICATIONINSIGHTS_CONNECTION_STRING` set on API, Analytics, and Web without printing its value. API and Web have `PublicLinks__BaseUrl=https://ziplio.io/`. **`OpenTelemetry__SamplingRatio` is missing on all three apps.** The committed Track D `appsettings.json` fallback is `1.0`; therefore this rollout did not establish the requested `0.25`. Michael was told before any configuration change, and none was made.

In workspace `log-shortener`, `AppRequests` after Web revision creation at 17:13:27 UTC showed 50 API, 21 Analytics, and 69 Web request records at the query time, with latest records after 17:16 UTC. `AppDependencies` after Analytics revision creation at 17:12:14 UTC showed two `click.outbox.publish` Kafka producer spans from `ca-shortener-analytics`, latest at 17:12:33 UTC. These counts verify ingestion from the new service revisions at query time; no `click.kafka.consume` span was observed in that window.

The browser inventory exposed no authenticated browser session. A signed-in managed-link Insights page was therefore **not verified** in this run; public smoke and telemetry do not establish that customer UI acceptance. No credential or customer link was read to bypass that boundary.

Final read-only Azure checks showed the migration job and three apps on the planned digests, all four resource provisioning states `Succeeded`, and API revision `ca-shortener-api--0000007`, Analytics revision `ca-shortener-analytics--0000001`, and Web revision `ca-shortener-web--0000007` each `Running/Healthy`. API still maps `VisitorHash__DailySecret` to `visitor-hash-secret`. The `lock-shortener-cannot-delete` lock remains `CanNotDelete`; it was never removed for this rollout. No rollback occurred.

The execution followed the approved migration job → API → Analytics → Web order. The rollback rule was held at every app health gate and public smoke gate; no gate failed, so no previous image was restored. The additive database migration remains in place, and the previous immutable digests remain recorded above for a focused rollback if later needed.

## IP redaction

No operator IP address or secret value belongs in this validation note. The temporary operator Key Vault firewall rule from the earlier secret prerequisite was removed. Two occurrences of the operator public IP in `2026-10-14-1017-track-g-visitor-hash-secret-rollout-plan.md` were replaced with `[operator IP redacted]` in this commit, without printing the address. DNS records and documented Azure network ranges are operational configuration, not operator-IP evidence.
