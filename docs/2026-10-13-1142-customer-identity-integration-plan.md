# Customer identity integration plan — 2026-10-13 11:42 CDT

Purpose: implement the copied 11:35 customer External ID handoff on `codex/workspace-authorization` while preserving private local state.

Scope: one configured identity provider per deployment; customer JWT validation and first-call workspace provisioning; Web workspace dashboard; configuration examples; automated validation and evidence. Portal changes, Google federation, deployment, and `Schwab_local` remain outside this implementation.

Tasks:
1. Record the identity architecture decision before changing authentication code.
2. Add API customer authentication and fail-closed identity claims; keep the workforce path selectable.
3. Add idempotent API-owned personal workspace provisioning and discovery.
4. Route the Web dashboard through the selected workspace and retain guest behavior and automatic updates.
5. Add tests, run the solution checks and guarded integration checks, then document the results.

Acceptance: copied handoff has its own commit; all intended files are committed; solution build and tests pass; customer token and database isolation tests cover the critical boundaries; provider and operator gates are accurately reported.

Outcome: implemented on this branch. The customer bearer handler, API provisioning, Web workspace routing, examples, and regression tests are complete. Automated checks passed. Real customer callback, token version, logout, and Google federation await the operator and live browser verification; details are in the dated validation note.
