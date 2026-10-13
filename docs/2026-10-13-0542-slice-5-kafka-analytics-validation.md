# Slice 5 Kafka analytics implementation and validation — 2026-10-13 05:42 CDT

## Implementation

Managed redirect resolution inserts one `click_outbox` row into PostgreSQL before the API returns 302. The insert selects tenant ID, mapping ID, and code from the persisted managed mapping and checks the resolved tenant. If PostgreSQL cannot commit this capture, the API returns an error instead of an uncounted redirect. Guest redirects create no row. Redis remains a redirect cache; even a managed cache hit performs the PostgreSQL capture.

The version 1 JSON event contains `Version`, `EventId`, `TenantId`, `MappingId`, `ShortCode`, `OccurredAtUtc`, and an optional trace ID. It contains no IP address, user agent, referrer, or requester identity. The Analytics publisher locks a pending outbox row, publishes it to Kafka with all-replica acknowledgement and the mapping ID as partition key, then marks the row published. It retries failures with bounded backoff, up to five attempts; a terminal row retains `failed_at_utc` and a sanitized error type. Kafka is not needed for API redirect success once PostgreSQL capture commits.

The consumer commits Kafka offsets only after a PostgreSQL transaction inserts the unique event ID and increments the tenant-matched managed mapping's count. Replaying an event finds its ID already processed and leaves the count unchanged. Invalid JSON, unsupported versions, invalid identities, and tenant/mapping mismatches are stored in `click_dead_letters` by topic, partition, offset, event ID, and reason code; raw payloads are not stored. Transient processing failures seek the same offset and retry five times. If dead-letter storage is unavailable, the worker retains the offset and retries. This is at-least-once delivery with transactional deduplication of aggregate writes, not end-to-end exactly-once processing.

## Configuration and operations

- Apply checked-in EF migrations before starting the API and worker. Both use `ConnectionStrings__ShortenerDb`.
- Configure `Kafka__BootstrapServers`, `Kafka__Topic`, and `Kafka__ConsumerGroup` for the Analytics worker. Create the topic with the intended partition and retention policy before starting it. Keep the consumer group stable across restarts.
- Inspect `click_outbox` for pending rows (`published_at_utc IS NULL AND failed_at_utc IS NULL`), terminal failures (`failed_at_utc IS NOT NULL`), and `click_dead_letters` for poison records. Inspect reason codes and broker/database health before resetting a terminal outbox row's `failed_at_utc` and `next_attempt_at_utc` for replay. Replayed published messages are safe for counts because `processed_click_events.event_id` is unique.
- The API emits `shortener.clicks.captured`. The worker emits `published`, `consumed`, `retried`, `deduplicated`, `failed`, pending outbox count/age, and consumer lag measurements. Structured logs include event, tenant, mapping, and trace IDs when present. The meter names are `Shortener.ClickCapture` and `Shortener.ClickAnalytics`; an exporter/alerting system still needs deployment configuration.
- Analytics reads are eventually consistent. A crash after Kafka acknowledgement but before marking the outbox published can publish the same event again; deduplication prevents an extra count. Terminal failures need operator repair/replay. No numeric throughput or latency claim has been measured.

## Local validation

The new migration was applied only to guarded disposable PostgreSQL database `Schwab_test_local`. An isolated Redis instance at loopback port 63652 and an isolated Apache Kafka 4.1.2 broker at loopback port 63653 were used. The existing application database and unrelated Kafka/Redis containers were not reset. The integration test uses a unique topic and consumer groups. It observed managed and guest API redirects, a failed publish and retry, successful broker publish and consume, replay with no double count, and tenant mismatch dead lettering. The focused integration project passed 14/14 tests. The full solution built with zero warnings/errors and passed 55/55 tests (24 unit, 14 integration, 17 Web).

## Remaining gates

Live Entra policy checks, deployment, monitoring exporter/alerts, production Kafka configuration, capacity testing, and operational replay drills are outside this slice's local validation. The test broker uses a single local replica; it does not establish production replication or availability.
