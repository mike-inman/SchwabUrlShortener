# Image rollback runbook — 2026-10-13 19:35 CDT

Scope: `prod` API, Web, and Analytics Container Apps. This is a documented process; no deployment or rollback has run in this slice. Web uses single-revision sticky sessions, so a traffic-split rollback is unavailable.

1. Before each deploy, record the Git commit, exact `SERVICE_API_IMAGE`, `SERVICE_WEB_IMAGE`, and `SERVICE_ANALYTICS_IMAGE` **digests**, current migration version, deployment time, and successful smoke results in the deployment record. Keep the previous known-good digests.
2. If a release fails, inspect health and logs and confirm the previous binaries are compatible with the current database schema. Schema migrations can be forward-only; do not assume image rollback also rolls back data.
3. After approval, set the three `prod` azd image values to the previous known-good digests, preview the P2 app changes, and redeploy the affected apps. Do not rebuild a tag or use a mutable tag as a rollback target.
4. Verify API/Web readiness, guest create and redirect, WebSocket live connection, managed-link authorization, and Analytics consumption. Record the before/after digests, replica health, and any residual incident.
5. If schema compatibility prevents rollback, keep the current deployment isolated and follow the recovery runbook rather than attempting an unreviewed database down-migration.

Production upgrade: a separate revision or second environment can support staged rollout when sticky-session and cost requirements allow it.
