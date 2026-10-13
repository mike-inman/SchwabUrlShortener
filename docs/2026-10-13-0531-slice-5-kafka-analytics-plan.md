# Slice 5 Kafka analytics plan — 2026-10-13 05:31 CDT

## Purpose and boundary

Implement the committed Slice 5 handoff in `/Users/michaelinman/Repos/SchwabUrlShortener-slice5` on `codex/slice-5-kafka-analytics`, starting clean from `a6336e42996268dbf1cf603530b7109d912e945d`. Preserve the canonical and Slice 1–4 worktrees, credentials, existing containers, and application data.

## Tasks

1. Add a versioned managed-click event and PostgreSQL outbox. A managed redirect must commit click intent before returning 302; a guest redirect must create no event.
2. Publish outbox rows to Kafka with all-replica acknowledgement, a stable mapping partition key, bounded retry, and inspectable terminal failures.
3. Consume Kafka manually, deduplicate event IDs in the same PostgreSQL transaction as tenant-scoped aggregate updates, and retain poison-message records without payloads.
4. Add focused tests against the guarded `Schwab_test_local` database and an isolated Kafka broker, then run solution checks. Update the README and dated validation record.

## Acceptance criteria

- PostgreSQL remains authoritative for mappings, click capture, and aggregate counts; Redis remains only a redirect cache.
- A successful managed redirect has a durable outbox record, and consumer replay cannot double-count.
- Guest redirects do not produce click events or analytics.
- Publishing, consumption, retries, terminal failures, and backlog are observable without raw credentials or payload logs.
- Real Kafka/PostgreSQL checks and remaining operational limits are recorded before the branch is committed.

## Outcome

Implemented the PostgreSQL outbox, versioned managed-click event, Kafka publisher/consumer, tenant-scoped deduplicated aggregate update, failure records, metrics, and guarded real-service tests. The focused integration project passed 14/14 tests against `Schwab_test_local`, isolated Redis, and isolated Kafka. `dotnet build SchwabUrlShortener.slnx` passed with zero warnings and errors; `dotnet test SchwabUrlShortener.slnx` passed 55/55 tests (24 unit, 14 integration, 17 Web). The committed handoff format checker passed. See `docs/2026-10-13-0542-slice-5-kafka-analytics-validation.md` for delivery and operator details. Commit evidence is the Slice 5 branch tip.
