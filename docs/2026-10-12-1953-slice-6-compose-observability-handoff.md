## Slice 6 — Docker Compose and Observability Handoff
Status: drafted on 2026-10-12
You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, current commit, and `git status --short --branch` before edits.
- Preserve all existing modified and untracked work. Do not reset, stash, discard, or stage unrelated files.
- Do not modify `.env*`, local agent configuration, credentials, or the untracked assignment PDF.
- Use real PostgreSQL, Redis, and Kafka services; do not replace them with mocks or canned containers.
- Start after Slices 2–5 are integrated.

Task: Provide a reproducible local Docker Compose environment for the complete application and its production-minded dependencies.

Goal:
Let an engineer start and health-check PostgreSQL, Redis, Kafka, API, Analytics worker, and Blazor Server using documented configuration without committing secrets.

Important context:
- The repository currently has no root `docker-compose.yml`.
- Service-specific application configuration and worker/API health checks may need to be completed before composition.
- Entra registration and credentials are operator-provided. No secrets may be embedded in Compose, tracked examples, logs, or handoff output.

In scope only:
1. Add a root Compose definition for PostgreSQL, Redis, Kafka, API, Analytics, and Web services.
2. Add container build definitions only for projects that need them and health/dependency checks based on real readiness.
3. Add safe configuration examples containing names and placeholders only.
4. Add structured service health/readiness and logging configuration sufficient to diagnose dependency failures.
5. Document startup, migration, Entra configuration, health checks, shutdown, and local test commands.

Out of scope:
- Cloud deployment, production secrets, Kubernetes, multi-region infrastructure, or live tenant provisioning.
- Weakening API authentication to make a local demo convenient.
- Checking in `.env`, changing existing `.env*`, or changing local credentials.
- Using startup sleeps instead of readiness checks or reporting container-started as service-ready.

Required discovery before edits:
- Inspect the final API, Web, Analytics, Infrastructure project files and configuration.
- Inspect `.gitignore`, `.dockerignore`, Dockerfiles (if present), and local Compose conventions without revealing secret values.
- Confirm the selected Kafka broker image and health-check command are supported by the chosen image/version.
- Review the integration outcomes from Slices 2–5, including migration and worker execution requirements.
- Review dirty diffs for any composition/configuration file before editing.

Implementation requirements:
- Provide explicit service health checks and dependency readiness; database migrations must be repeatable and must not be confused with app startup health.
- Use non-secret environment variables or Compose secrets/configuration mechanisms; tracked files contain only placeholders.
- Ensure API and worker fail visibly when required dependencies are unavailable; never fall back to in-memory storage or fake broker behavior.
- Keep host ports/configuration minimal and document how an operator supplies Entra settings.
- Ensure logs go to standard output in a structured form and redact secrets.
- Do not add polling loops in the application for health or progress; health probes are bounded service checks, not UI polling.

Exception / observability requirements:
- Emit sanitized startup/shutdown and dependency health events with service identity and correlation where available.
- Keep connection strings and credentials out of container logs and health response bodies.
- Surface dependency-unavailable status accurately; no success-shaped startup fallback.

Realtime / no-polling requirements:
- Not applicable: Compose health checks are infrastructure readiness probes, not operator-facing asynchronous progress. Do not add browser polling or app timer-refetch behavior.

Testing:
- Validate Compose syntax with the installed Compose CLI configuration and run the repository’s container stack if Docker is available.
- Exercise service health endpoints and a real create/redirect/analytics flow through the composed stack.
- Confirm invalid/missing required configuration fails clearly without exposing secret values.
- Run solution build/tests after the integrated stack is healthy.
- Report which services actually ran and identify any unavailable real dependencies; Compose parsing alone is not runtime verification.

Documentation:
- Add a dated implementation/validation note and update README run instructions surgically.
- Record the exact commands run and distinguish local container verification from production deployment/acceptance.

Completion criteria:
- [ ] Compose defines all required runtime services with readiness checks.
- [ ] PostgreSQL migrations and real Redis/Kafka integrations run without fake fallbacks.
- [ ] No secret value or local credential file is committed or changed.
- [ ] A documented end-to-end local flow succeeds and its real-service boundaries are stated.
