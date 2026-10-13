# Slice 6 Compose and observability completion plan — 2026-10-13 12:58 CDT

Purpose: Finish the remaining Slice 6 acceptance gates from the existing uncommitted Compose work without changing local credentials or unrelated files.

Scope: Add real dependency health checks and startup ordering for PostgreSQL, Redis, Kafka, API, Web, and Analytics; make service logs inspectable and sanitized; provision a reproducible isolated integration-test path; run a composed managed redirect through the real worker and broker; document evidence. Preserve existing `.env*` files and leave all work uncommitted.

Tasks:
1. Verify image-supported Kafka commands and review current readiness/logging behavior.
2. Add bounded service health probes, explicit Kafka topic creation, and dependency readiness without startup sleeps.
3. Add an isolated disposable test database and test runner connected to real composed dependencies.
4. Validate missing configuration, build, full tests, public guest redirect, managed click capture/publish/consume, and startup/shutdown evidence.
5. Update dated validation notes and the Slice 6 handoff status, with unresolved external Entra and deployment gates stated separately.

Acceptance: All required services report real readiness; migrations are explicit and repeatable; a local composed click reaches the analytics aggregate; the guarded integration suite passes against disposable PostgreSQL/Redis/Kafka; no secret or unrelated local work is changed or committed.

Outcome (2026-10-13 13:23 CDT): Local Slice 6 acceptance criteria are met. The guarded real-service test profile passed 58/58 tests. PostgreSQL, Redis, Kafka, Caddy, API, Web, and Analytics were healthy. A composed managed redirect populated Redis, committed one outbox row, published/consumed one Kafka event, and advanced its aggregate once. Web readiness failed during an isolated API stop and recovered after restart; an unavailable-broker Analytics probe failed. Missing API/worker configuration failed with named-setting diagnostics. After a final Web logging edit, the solution built with zero warnings and Web tests passed 19/19. Final images rebuilt with the native PostgreSQL dependency, and a fresh seven-service smoke passed with no missing-library warning. The dated validation record contains exact boundaries and remaining external gates. Both disposable stacks were removed. Local credentials and unrelated files were preserved. No commit was made.
