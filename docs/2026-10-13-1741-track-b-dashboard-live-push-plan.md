# Track B dashboard live push plan — 2026-10-13 17:41 CDT

## Purpose

Replace the dashboard's five-second API sampling with Redis change hints delivered through the existing Blazor circuit.

## Scope and tasks

1. Add the shared change contract, best-effort Redis publisher, and API and Analytics publish points.
2. Add one Web Redis subscriber with scoped in-process fan-out, reconnect resync, and connection state.
3. Subscribe the dashboard to its authenticated tenant or API-returned workspace, debounce hints, and remove the polling loop.
4. Add focused tests, Compose settings, README guidance, and validation evidence.

## Acceptance

Idle dashboards acquire no token and make no API request; applied changes and new managed links trigger scoped refresh; failed publish does not alter create or Kafka offset behavior; build and tests pass. Record any live-service limits separately.

## Outcome

Implemented the scoped publisher, single Web subscriber, dashboard subscription and debounce, Compose settings, and focused tests. See [validation](2026-10-13-1800-track-b-dashboard-live-push-validation.md) for check results and operational limits.
