# Schwab Compose grouping validation — 2026-10-13 09:03 CDT

## Purpose

Record the local move of Schwab-only PostgreSQL, Redis, and Kafka from standalone Docker containers into the `schwab-url-shortener` Compose project. This validates infrastructure grouping, not the complete application bundle or customer identity flow.

## Changes

- Added root `compose.yaml` with stable Schwab names, loopback ports 63651–63653, pinned images matching the prior containers, health checks, and explicit external data volumes.
- Reused the original PostgreSQL, Redis, and Kafka data/config/secrets volumes. Their host-specific identifiers are stored only in the private `0600` local file `/Users/michaelinman/.config/schwab-url-shortener/compose.env`, which points to the pre-existing PostgreSQL password file without copying or printing its value.
- Used Docker's existing `bridge` network because Docker reported that all predefined address pools were fully subnetted when Compose attempted a new network. This matches the three prior standalone containers. The future full application Compose bundle must address inter-container service discovery rather than assuming a new network is available.
- Removed the failed stopped earlier Kafka container and the three stopped standalone predecessors with `docker rm` only. No `-v`, volume prune, database deletion, or AIF operation was used. A pre-migration PostgreSQL dump remains private at `/private/tmp/schwab-compose-pre-migration-2026-10-13.sql`.

## Verification

- `docker compose ... config --quiet`: passed.
- `docker compose ls --all --format json`: `schwab-url-shortener` reported `running(3)` with this worktree's `compose.yaml`.
- `docker compose ... ps`: PostgreSQL, Redis, and Kafka all reported healthy on the original loopback ports.
- Compared every new volume attachment with the original identifiers: exact match for PostgreSQL, Redis, and Kafka data/config/secrets.
- PostgreSQL `pg_isready` passed; read-only database-name query showed `Schwab_local` and `Schwab_test_local` still present.
- Redis `PING` returned `PONG`.
- Host `kcat -b 127.0.0.1:63653 -L` obtained Kafka broker metadata. No topics were present. The in-container Kafka client could not follow the existing host-advertised `localhost:63653` address, so the container health probe checks its local listener. This does not substitute for the host metadata check.

## Current use and remaining work

To inspect or restart this exact local group, use `docker compose --env-file /Users/michaelinman/.config/schwab-url-shortener/compose.env -f compose.yaml ps` or `up -d` from this worktree. Never use `down -v` or prune volumes.

This Compose file currently groups infrastructure only. The existing dated Slice 6 Compose handoff still needs API, Web, Analytics worker, explicit migration orchestration, safe first-run volume creation, developer configuration instructions, fresh-machine checks, and live customer registration/Google sign-in acceptance. Avoid interpreting container health as application readiness or Kubernetes readiness. The Compose network design must be revisited for container-to-container traffic when those application services are added.
