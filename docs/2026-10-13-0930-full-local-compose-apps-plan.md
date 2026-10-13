# Full local Compose application bundle plan — 2026-10-13 09:30 CDT

## Purpose

Implement the 09:20 handoff in the existing `codex/workspace-authorization` worktree. Add reproducible API, Web, Analytics, and explicitly invoked migration containers while preserving the existing Schwab data volumes, databases, credentials, and unrelated services.

## Scope and tasks

1. Capture current container, volume, database, broker, network, and repository state. Select a nonconflicting configurable subnet for a Schwab-only Compose network.
2. Add multi-stage .NET 10 images for the three apps and a migration job. Extend Compose with internal service discovery, dual Kafka listeners, health checks, and bounded startup dependencies. Keep credentials in ignored per-developer files or Docker secrets.
3. Validate Compose syntax and application builds. Exercise migration against only `Schwab_test_local`, then bring up the group, verify host/internal broker access, guest create/redirect, Web availability, and Analytics with real Kafka. Preserve V1/V2 behavior.
4. Document setup, HTTPS/OIDC preparation, explicit migration, stopping, backup/restore, observations, and remaining live identity/cluster gates. Stage only intended files and commit locally.

## Acceptance

- All six services appear under the Schwab Compose project and the three app images run from checked-in source.
- Existing data volumes and database names remain intact; app database migrations remain an explicit operator step.
- Guest flow, Kafka processing, health paths, and exact identity limitations are reported from observed local behavior.
- No secrets, passwords, certificates, tokens, or unrelated Docker changes are committed.

## Outcome

Implemented the six-service Compose bundle, optional HTTPS override, private configuration templates, explicit guarded migration job, Web and Analytics readiness checks, and fresh-machine instructions. All six long-running services were healthy after local startup. The guarded migration, guest API flow, V1/V2 containerized analytics, host/internal Kafka metadata, HTTP/HTTPS Blazor WebSocket upgrades, and OIDC redirect initiation were observed as recorded in `docs/2026-10-13-0942-full-local-compose-apps-validation.md`. The full solution built with zero warnings/errors and passed 60/60 tests. Application database migration, live sign-in, customer onboarding, and deployment remain separate gates.
