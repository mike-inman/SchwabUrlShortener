# Kubernetes readiness plan — 2026-10-13 07:34 CDT

## Purpose and current state

Plan a safe path from the current local services to a Kubernetes deployment. This is planning only; it does not authorize a cluster, deployment, database migration, credential change, or container change.

On 2026-10-13, `docker ps` showed the Schwab PostgreSQL, Redis, and Kafka dependency containers running. It showed no Schwab API, Web, or Analytics application container. This worktree has no Dockerfile, Compose definition, or Kubernetes manifests. The separate Slice 6 handoff already plans a complete local Compose stack and excludes Kubernetes delivery. The accounts foundation migration is still unfinished and unapplied.

## Strict separation from AI-Influencer-Factory

SchwabUrlShortener is a separate solution. Its local stack must use a dedicated Compose project, network, named volumes, container names, port assignments, and secret files. It must not connect to, depend on, reset, rename, or reuse any AI-Influencer-Factory container, database, broker, cache, volume, network, credential, or configuration. Running on the same Docker Desktop host is only host sharing; the project resources remain separate. If host-level isolation is required, use a separate Docker context or VM rather than sharing project resources.

For Kubernetes, plan a dedicated Schwab cluster and deployment account/resource boundary. A namespace in an AI-Influencer-Factory cluster is insufficient for the requested solution separation. Use separate registry access, workload identities, network policy, secrets, storage, databases, cache, broker, observability, and cost tracking. No Schwab manifest should reference an AI-Influencer-Factory endpoint or resource.

## Sequence and acceptance gates

1. Complete and validate the accounts foundation in its dedicated worktree. Preserve the existing application database and all unrelated services. Treat real PostgreSQL migration tests as a gate before any deployment design depends on the new schema.
2. Complete the separate Slice 6 local container track: reproducible images for API, Web, and Analytics; Compose wiring to PostgreSQL, Redis, and Kafka; safe configuration; readiness checks; and a real end-to-end local flow. Reuse those application images for Kubernetes. Do not equate a running container with a ready service.
3. Choose the dedicated Kubernetes target and stateful-service placement. Decide whether the target is local learning, AKS, or another cluster; choose Schwab-only registry access, ingress/TLS/DNS, and managed versus cluster-hosted PostgreSQL, Redis, and Kafka. Record cost, backup, recovery, and operational ownership before provisioning.
4. Define deployment resources for API, Web, and Analytics, with separate configuration, service accounts, resource requests/limits, rollout settings, and startup/readiness/liveness checks. The API already exposes `/health/live` and `/health/ready`; add fit-for-purpose health signals for Web and worker before wiring probes. Blazor's server-side interactive circuits require a specific scale-out design: test WebSocket forwarding, session affinity and reconnect behavior, shared Data Protection keys, and Entra redirect/callback behavior through the chosen ingress before adding Web replicas.
5. Supply credentials through an approved secret mechanism without committing values. Keep local passwords and API keys intact during planning and migration. Pin images to immutable digests and restrict workload and network access to required dependencies.
6. Run database migrations as a controlled, single-run release operation after a verified backup and restore rehearsal. Do not have every API replica independently migrate the application database. Test rollback of application images separately from database forward repair.
7. Validate on an isolated nonproduction cluster: deploy and observe a real managed-link create/redirect/analytics flow, guest expiry, tenant isolation, dependency outages, rolling updates, pod restart, and restoration. Record exact evidence before considering production deployment.

## Open decisions

- Kubernetes provider and environment: local-only, AKS, or another target.
- Registry, ingress controller, TLS certificates, DNS, and Entra redirect URIs.
- Hosting and backup ownership for PostgreSQL, Redis, and Kafka.
- Secret provider, workload identity, observability destination, budget, and expected load.
- Whether production deployment is actually needed for this assignment after the local Compose flow is complete.

## Scope boundary

This plan does not modify the existing Slice 6 handoff or the accounts foundation migration. Any Kubernetes implementation needs its own dated handoff and validation record. No cluster, application image, Kubernetes resource, database, secret, or existing Docker service was changed by drafting this document.

## References checked 2026-10-13

- Kubernetes workload guidance: https://kubernetes.io/docs/concepts/workloads/
- Kubernetes startup, readiness, and liveness probes: https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/
- Kubernetes StatefulSets and persistent storage: https://kubernetes.io/docs/concepts/workloads/controllers/statefulset/
- Kubernetes Secrets and encryption at rest: https://kubernetes.io/docs/concepts/configuration/secret/ and https://kubernetes.io/docs/tasks/administer-cluster/encrypt-data/
- Microsoft Blazor server-side SignalR scaling guidance: https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-10.0
