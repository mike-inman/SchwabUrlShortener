# Track C Well-Architected P1 plan — 2026-10-13 19:35 CDT

## Purpose

Bring the prepared single-environment P1 Bicep in line with the 19:35 contract and Well-Architected review, then produce a read-only what-if. Michael has not approved P1 provisioning.

## Scope

Only `infra/**` and dated documentation under `docs/` and `docs/runbooks/`. Preserve the canonical checkout, other worktrees, ignored azd environments, secrets, databases, and all existing Azure resource groups. Do not register providers or create Azure resources before P1 approval.

## Tasks

1. Recopy and commit the revised contract, Track C handoff, and Well-Architected review before implementation.
2. Add the five standard tags to taggable P1 resources and the new group. Add a resource-group `CanNotDelete` lock in a separate last P1 step.
3. Set explicit minimum TLS and exposure properties. Model only free foundational CSPM at subscription scope; review subscription provider state and record a read-only secure-score check after provisioning.
4. Add a P1 Azure Monitor workbook with panels for requests, latency, errors, replicas, Analytics lag, outbox age, and PostgreSQL health. Panels dependent on Track D telemetry must say so.
5. Add dated recovery, rollback, and secret-rotation runbooks. Check Microsoft guidance for Web managed-identity federation with the multitenant registration and record the P2 operator step. Record the trial end date from the contract.
6. Build/lint Bicep, inspect staged paths, run a subscription-scope what-if with disposable placeholder secrets, and record the exact prediction and any preview limitations. Leave provisioning pending Michael's approval.

## Acceptance criteria

- P1 template and preview cover the new controls without enabling a paid Defender plan or applying a lock before the rest of P1.
- No secret value is read, displayed, logged, or committed. `WEB_CLIENT_SECRET` presence is checked by exit status only.
- The what-if targets only the new group plus the explicitly requested subscription-level free CSPM setting and predicts no change to an existing resource group.
- Validation distinguishes authored configuration from live posture, alerts, and recovery tests.

## Outcome

The three revised source documents were copied and committed as `8098573`. The P1 templates, workbook, and runbooks were authored. The 2026-10-13 preview predicts 66 creates and no update/delete: one new resource group, one free subscription posture setting, and 64 resources within the new group. The lock is modeled after the rest of P1. This is a prediction, not a deployed control. Final offline validation and implementation commit are recorded in the infrastructure validation note.
