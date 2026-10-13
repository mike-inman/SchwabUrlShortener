# Schwab Compose grouping plan — 2026-10-13 08:50 CDT

## Purpose

Group Schwab's standalone PostgreSQL, Redis, and Kafka services under one Docker Compose project named `schwab-url-shortener`, preserving existing storage and loopback ports. This is the infrastructure stage of the full local application bundle; API, Web, Analytics, migrations, and customer identity onboarding remain follow-up work under the existing Compose handoff.

## Starting state

- Worktree: `codex/workspace-authorization` at `50896637c93ac16a3e668874dc00a0cc30b1da4c`, clean at inspection.
- Standalone PostgreSQL uses named volume `schwab-url-shortener-pgdata`. Redis and the active Kafka broker use anonymous volumes. Their exact volume IDs are host-local and are not checked into Git.
- Existing dedicated loopback ports are PostgreSQL 63651, Redis 63652, Kafka 63653. No AI-Influencer-Factory container is in scope.
- A failed stopped Kafka attempt was removed without `-v`; its volumes were preserved for recovery.

## Migration and recovery

1. Record exact container IDs, image versions, configuration keys, ports, and volume IDs without printing secret values. Keep the existing PostgreSQL password file untouched.
2. Validate the Compose file with the current host's existing volume IDs supplied through a private untracked env file.
3. Rename the three running standalone containers to `-standalone-backup`, stop them, and start the Compose group using those same data volumes and ports. Never use `docker compose down -v`, `docker rm -v`, or volume prune.
4. Verify all three new containers are healthy and retain the same volumes and ports. Confirm a database and Kafka read without writing or printing credentials.
5. Only after successful verification, remove the stopped standalone backup containers without `-v`; retain their volumes. If verification fails, stop the new group and restore the old container names and processes using the unchanged volumes.

## Acceptance

- Docker Desktop displays a `schwab-url-shortener` group with PostgreSQL, Redis, and Kafka beneath it.
- Existing PostgreSQL data, Redis volume, Kafka data/config/secrets volumes, loopback ports, and local password file remain intact.
- No AIF or unrelated container, volume, network, or secret is changed.
- The full app Compose bundle, migrations, and external identity flow remain explicit next gates; this infrastructure grouping alone is not fresh-machine acceptance.

## Outcome — 2026-10-13 09:03 CDT

The three services now run under the `schwab-url-shortener` Compose project with their original data volumes and loopback ports. Docker's default address pool was exhausted, so the Compose group uses the existing default bridge network. All three services passed health checks; PostgreSQL databases and host Kafka metadata were observed read-only. See `docs/2026-10-13-0903-schwab-compose-grouping-validation.md` for exact evidence and remaining full-bundle work.
