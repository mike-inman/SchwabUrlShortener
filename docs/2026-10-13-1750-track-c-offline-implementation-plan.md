# Track C offline infrastructure implementation plan — 2026-10-13 17:50 CDT

## Purpose

Author the corrected Azure staging contract in the isolated `codex/azure-infra` worktree. Deployment waits for an explicit subscription confirmation in chat.

## Scope

- Change only `infra/**`, `azure.yaml`, and dated documents under `docs/`.
- Define private staging infrastructure, managed identities, data services, Container Apps, Front Door, and the migration job in Bicep.
- Build API, Web, and Analytics images remotely with ACR targets and deploy immutable digests through azd.
- Keep provider registration, application code, local databases, Docker services, secrets, and other worktrees untouched.

## Work

1. Verify the corrected contract, current resource schemas, and available local tooling.
2. Author modules, staging parameters, image build script, and `azure.yaml`.
3. Compile and lint Bicep, validate shell and azd configuration, and review staged paths for secrets.
4. Record offline evidence, remaining live gates, cost assumptions, and exact operator actions in a dated validation note.

## Acceptance

- Corrected contract names and settings appear consistently across Bicep, azd, and script.
- Images are built with `az acr build --target` and resolved to digests.
- Offline checks pass with any remaining warnings or unsupported live claims documented.
- No Azure resource is provisioned until the subscription is explicitly confirmed.

## Outcome — 2026-10-13 17:58 CDT

The corrected contract and handoff were copied and committed. C1–C4 Bicep modules, staging parameters, azd prebuilt-image mapping, image build script, and the infrastructure guide were authored. Bicep build/lint, parameter compilation with a disposable placeholder, shell syntax, YAML/JSON parsing, and diff whitespace checks passed. The confirmed Azure account and nonexistent staging resource group were checked read-only.

No Azure resource was created. The contract's Front Door origin host header conflicts with its `AllowedHosts` settings, so the handoff's “stop if the contract is wrong” rule holds deployment. The dated validation note records the exact mismatch and subsequent gates.
