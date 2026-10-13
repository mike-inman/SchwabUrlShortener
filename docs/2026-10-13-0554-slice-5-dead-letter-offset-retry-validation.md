# Slice 5 dead-letter offset retry fix — 2026-10-13 05:54 CDT

## Purpose and scope

Close the consumer gap found before merge: an invalid Kafka event could fail while being written to `click_dead_letters` after the local consumer position advanced. A later message on the same partition might then be committed, skipping the invalid event. This change is limited to consumer offset handling and a regression test.

## Outcome

`ClickKafkaConsumer` now retains the consumed topic, partition, and offset until its database effect and Kafka offset commit complete. Failed dead-letter persistence or commit seeks that same offset and returns a retry outcome. Every subsequent consume call seeks the retained offset before reading; if seek itself fails, the marker remains and the worker cannot consume a later message on that instance. A different delivered offset while one is pending is not processed or committed. Successful processing or dead-letter commit clears the marker.

## Validation

The new real integration test publishes malformed JSON at offset 0 and a valid managed click at offset 1 to a unique, single-partition Kafka topic. It holds an exclusive lock on `click_dead_letters` in the guarded `Schwab_test_local` database and uses a one-second command timeout. Two dead-letter writes fail and return retry while the managed hit count stays zero. After releasing the lock, the test observes offset 0 dead-lettered before offset 1 is applied, then verifies one dead-letter row, one processed event, and one managed hit.

- Focused regression: passed, 1/1, using isolated real Kafka on loopback port 63653 and PostgreSQL `Schwab_test_local`.
- `dotnet build SchwabUrlShortener.slnx`: passed, zero warnings and errors.
- `dotnet test SchwabUrlShortener.slnx --no-build`: passed, 56/56 (24 unit, 15 integration, 17 Web), with isolated real Kafka, Redis, and guarded PostgreSQL settings.

No application database, existing credentials, or deployment was changed. This local test does not exercise a Kafka rebalance or a real seek/commit failure; the retained marker is the fail-closed path for those errors. Deployment and operational monitoring remain open as described in the original Slice 5 validation note.
