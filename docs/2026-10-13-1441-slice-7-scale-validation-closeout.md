# Slice 7 scale validation and closeout — 2026-10-13 14:41 CDT

## Purpose, plan, and scope

Validate the integrated local shortener with a small, reproducible load profile and bounded dependency faults. Acceptance is a dated record of measured correctness, throughput, latency, cache/database/broker observations, and explicit production gaps. This is a single-host baseline, with no invented service-level objective. The worktree was clean at `5a57e758453391b840dfc532648025e306c100c6` on `codex/slice-7-scale-validation`, based on published `main` `f7102648ae48208f1acadf375a17bc59ab521dde`. No unrelated worktree, existing `schwab-url-shortener-*` container/volume, credential file, or local key was changed.

Tasks: (1) build and test the solution, (2) start a fresh disposable Compose project, (3) exercise a capped HTTP client, (4) verify guest expiry and analytics exclusion, (5) stop and restart each disposable dependency briefly, (6) reconcile README and architecture claims, (7) inspect and commit intended paths only.

## Environment and reproducible commands

- Local macOS Docker Desktop 29.4.3 / Compose 5.1.4; .NET SDK 10.0.107; Docker VM reported 16 CPUs and 8,322,625,536 bytes memory. Images in this stack: PostgreSQL 17 Alpine, Redis 7.4 Alpine, Kafka 4.1.0, and .NET 10 API/Web/worker. The broker has one node and the topic three partitions with one replica. The network used the checked-in `deploy/network.override.example.yaml` subnet; the only published test port was loopback `18087`.
- Fresh project name: `schwab-slice7-check`. `deploy/local.env.example` supplied only documented placeholders, with no real Entra tenant or bearer token. Its volumes were new and project-prefixed. The default API guest path, real PostgreSQL/Redis/Kafka, real API/proxy, and Analytics worker ran. Managed redirects used a fresh row and legacy workspace inserted directly into this disposable database; authenticated managed creation and analytics reads were not exercised.
- Start: `LOCAL_PORT=18087 PUBLIC_BASE_URL=http://localhost:18087/ docker compose -p schwab-slice7-check --env-file deploy/local.env.example -f compose.yaml -f compose.local.yaml -f compose.test.yaml -f deploy/network.override.example.yaml --profile test up --build -d --quiet-pull`.
- Build: `MSBUILDDISABLENODEREUSE=1 dotnet build SchwabUrlShortener.slnx --nologo -m:1 /nodeReuse:false` passed with zero warnings/errors. An unconfigured host `dotnet test SchwabUrlShortener.slnx --nologo -m:1 /nodeReuse:false` ran unit 25/25 and Web 38/38 but failed 19/20 integration cases at the intentional guarded-real-database precondition. This host result is not counted as passing integration evidence.
- The disposable `tests` container ran that same full solution test against its separate migrated `Schwab_test_local` database and real Redis/Kafka: integration **20/20**, unit **25/25**, Web **38/38**, **83/83** total, exit 0. API, Web, Analytics, PostgreSQL, Redis, Kafka, and proxy were healthy before fault checks. Integration auth remains a test-only handler, not live Entra.
- Run the opt-in client only against a fresh local test project: `python3 scripts/load-local.py --local-test-only --origin http://localhost:18087 --managed-code slice7m01 --requests 40 --concurrency 4`. It accepts at most 500 requests per case and concurrency 8, uses a five-second request timeout, does not follow redirects, and prints only aggregate status/timing information. Optional authenticated managed-detail and analytics cases use `SCHWAB_LOAD_BEARER_TOKEN` from the process environment; they were skipped here because no disposable valid token was available. Do not point it at existing local user links.

## Bounded measurements

The first run performed 40 measured requests **per case**, concurrency 4. Guest creation generated a fresh link on every request; redirect cases were warmed by one request each. Results are client-observed, sequential cases, with no independent saturation search. Each case had 0 HTTP/network errors.

| Case | Elapsed | Throughput | p50 | p95 | p99 | Error rate |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Guest create | 0.258 s | 155.20 requests/s | 24.12 ms | 40.64 ms | 51.92 ms | 0/40 |
| Guest redirect, warm | 0.125 s | 319.74 requests/s | 12.17 ms | 15.90 ms | 20.83 ms | 0/40 |
| Managed redirect, warm | 0.196 s | 203.75 requests/s | 17.75 ms | 34.06 ms | 37.23 ms | 0/40 |

A second 20-request-per-case run at concurrency 4 also had zero errors. Its guest create / warm guest redirect / warm managed redirect throughputs were 181.87 / 340.80 / 254.83 requests/s and p95 latencies 44.31 / 15.65 / 24.14 ms. Redis `keyspace_hits` changed from 5 to 47 and `keyspace_misses` stayed at 1 around that second run: **42/42 observed Redis lookups hit** (100% for this warmed interval). This is an interval counter ratio, not a systemwide production cache hit rate.

After the first run, the disposable application database was 8,468,147 bytes and had 7 connections in `pg_stat_activity`. One `docker stats --no-stream` snapshot showed API 85.9 MiB, PostgreSQL 47.02 MiB, Redis 4.574 MiB, Kafka 339 MiB, and Analytics 45 MiB; CPU percentages were 0.08%, 0.12%, 0.56%, 3.13%, and 0.45% respectively. These point-in-time values do not establish peaks or resource limits. The Kafka group `shortener-analytics-v1` showed partition 1 current/log-end offsets 42/42 and lag 0; partitions 0 and 2 had no committed offset and no messages. Outbox rows were 42/42 published at that observation. No broker replication or durable multi-node recovery was tested.

## Correctness and bounded failures

- A fresh guest link returned 201 then 302. Its database row retained `hit_count=0` and `last_accessed_at_utc IS NULL`; the outbox count remained 42 after its redirect. This corroborates the no-analytics path in the real app database. A distinct guest row with `created_at_utc` 169 hours in the past and `expires_at_utc` exactly 168 hours after creation returned 404. This used only fresh disposable data; no production clock or row was changed. The full real-service suite also covers the seven-day boundary.
- Redis stop: API readiness returned 503; an active guest link still returned 302 by PostgreSQL fallback. Redis restart restored readiness 200 and guest 302. An initial probe mistakenly targeted the expired test link and returned 404; it was repeated against the active guest link above.
- PostgreSQL stop: API readiness returned 503; a warm cached guest redirect returned 302, while a managed redirect returned 500 because durable click capture could not commit. PostgreSQL restart restored readiness 200 and managed redirect 302. The error was not counted as success.
- Kafka stop: managed redirect returned 302 after the outbox commit, and one unpublished row was observed in PostgreSQL. After Kafka restart, unpublished/unfailed rows returned to 0 and the managed aggregate count advanced to 43. This is one local interruption/recovery, not a long-duration backlog or poison-message drill.
- The first `127.0.0.1` HTTP probe returned 400 because this Compose profile allows host `localhost`; the script default was corrected to `http://localhost:18087`. No service defect or capacity result was inferred from the rejected host header.

## Implementation versus acceptance

The integrated application and its real local services passed the disposable test suite and the bounded checks above. Live Entra sign-in and delegated protected calls, cloud deployment, public DNS/TLS, production secrets, broker replication, observability export/alerts, and sustained load/recovery have not been verified. The load client did not measure authenticated management or analytics latency because no safe disposable token was configured. It also did not establish cold-cache latency, long-running contention, confidence intervals, or production throughput.

The benchmark emitted no bearer token, cookie, redirect destination, full URL, or response body. Aggregate outbox/consumer offsets establish completion, but an individual API request/event ID was not retained across worker logs for this run, so per-event trace correlation is an unverified observability gate.

To turn this baseline into acceptance gates, the operator must supply the deployment environment and hardware shape; guest/managed traffic mix; sustained and burst request rates; p95/p99 latency and error objectives; availability; analytics freshness and retention; recovery time/data-loss requirements; and representative link cardinality/cache working set. Then run a separate authorized workload in that environment with metrics export and alerting. No numeric SLO or “high-volume-ready” claim follows from this note.

Cleanup completed: `docker compose -p schwab-slice7-check ... down --volumes --remove-orphans` removed only that project and its four named volumes. A subsequent inventory still showed all six `schwab-url-shortener-*` containers and both named Schwab volumes. Commit identity and final Git state are reported in the handoff.
