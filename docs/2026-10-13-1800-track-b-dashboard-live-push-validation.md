# Track B dashboard live push validation — 2026-10-13 18:00 CDT

## Design and behavior

API managed creates publish only when the result is Created. The Analytics worker publishes only after a click aggregate was applied and its Kafka offset was committed. V1 events use tenant scope; V2 events use workspace scope. A Redis publish failure is counted and logged once by exception type, without failing the API operation or reprocessing a committed Kafka event.

`LinkChange` serializes the contract hint exactly as `{ "v": 1, "scope": "tenant"|"workspace", "scopeId": "<guid>", "shortCode": "<code>" }`. It carries no URL, count, user ID, or token. One Redis channel subscription per Web process fans out only to matching in-process scope subscriptions. A connection restoration invokes each subscriber once to resynchronize. Disconnection leaves the dashboard warning visible; it never starts a polling fallback.

The dashboard chooses a workforce tenant from its signed-in `tid` claim or a customer workspace from the list returned by the authorized API. The first hint schedules a refresh after the configured interval; hints while that refresh is pending are absorbed. The refresh uses the existing delegated-token API list request and diff. Workspace changes and disposal cancel a pending refresh. Selection, toasts, and reauthentication still use their existing paths. The server-side five-second `PeriodicTimer` is removed.

## Local checks

- `MSBUILDDISABLENODEREUSE=1 dotnet build SchwabUrlShortener.slnx --nologo -m:1 /nodeReuse:false`: passed with zero warnings and errors after the source changes made before this note.
- `rg 'PeriodicTimer' src/Shortener.Web`: no matches.
- Focused host Web suite without its real-Redis test: 39/39 passed. Unit suite: 30/30 passed.
- A fresh disposable Compose project named `schwab-trackb-check` used loopback port `18093`, `compose.yaml`, `compose.local.yaml`, `compose.test.yaml`, and `deploy/network.override.example.yaml`. The example subnet `10.251.42.0/24` overlapped the active Track A test project, so a temporary last-file override selected free `10.251.43.0/24`. No existing network was changed. The full suite passed 20 integration, 30 unit, and 40 Web tests (90/90). Afterwards, a nullable test annotation and the Analytics consumed-event log's scope field were changed without changing runtime control flow; the final host solution build passed with zero warnings and errors. The full suite was not rerun after those two edits.

## Real-service and end-to-end evidence

The real-Redis test constructs separate publishing and receiving connections, checks tenant and workspace routing (including equal IDs across scope kinds), failing callback isolation, disposal, and `shortener.live.received` measurement. The Compose test suite uses a guarded `Schwab_test_local` database and test-only API authentication handler.

In the disposable application database, a test-only legacy workspace and managed row `trackb1` were inserted. `GET http://localhost:18093/r/trackb1` returned 302 to `https://example.com/trackb`. PostgreSQL later showed `hit_count=1`, one outbox row, and one processed event; Analytics logged the V1 click as published and consumed. This exercises the real proxy, API, PostgreSQL, Kafka, Analytics, and Redis path. The direct insert bypassed authenticated managed creation; the integration suite covers that API path separately.

The running Web process returned 200 for `/` and API readiness returned 200. Redis `PUBSUB NUMSUB shortener:links:v1` returned `1`, consistent with one Web instance holding one channel subscription.

The signed-in browser dashboard and its exact API fetch count were not observed end-to-end because this disposable stack has no live Entra sign-in. The Web process's metric stream is not exported by this Compose setup; `shortener.live.received` was observed in the real-Redis integration process, not externally from the running Web process. No Azure deployment or multi-replica operational acceptance is claimed.

## Cleanup and remaining work

The disposable `schwab-trackb-check` project was removed with `down --volumes --remove-orphans`; its containers, named volumes, and network were deleted. Track A had not yet merged at the time of this note, so rebase and merge compatibility remain to be checked when it does.

## 2026-10-13 18:48 CDT — throttle repair and Track A integration

The dashboard now schedules one fetch after `LiveUpdates:DebounceMilliseconds` when the first signal arrives, absorbs later signals while that fetch is pending, and cancels the pending fetch on workspace switch or disposal. The new sustained-traffic test sends 25 signals 200 ms apart over five seconds with a 1000 ms interval and requires 4–6 fetches beyond the initial load. A separate test covers workspace switch and disposal cancellation. Both passed in the final suite.

Track B was rebased onto Track A at `11349b6`. Conflicts in Analytics, API, and Web startup, the Web project file, and the API integration test factory were resolved with both tracks' registrations, packages, scale-out configuration, and test settings preserved. The Track A appsettings startup test factories also received the required Track B live-update test settings. The duplicated Web Redis Compose setting was removed.

The final host `dotnet build SchwabUrlShortener.slnx -warnaserror --nologo` passed with zero warnings and errors. The full suite passed in a new disposable Compose project `schwab-trackb-accepted-check` using `Schwab_test_local`, loopback port `18096`, and an isolated `10.251.46.0/24` network: 27 integration, 30 unit, and 56 Web tests, 113/113 total. The test container exited 0; API, Web, and Analytics containers were healthy. The project was removed with `down --volumes --remove-orphans`, including its dedicated network and volumes. No existing Compose project or database was changed.

This run verifies local Compose and test-host behavior. A live Entra dashboard session, Azure deployment, and multi-replica operation remain unverified.
