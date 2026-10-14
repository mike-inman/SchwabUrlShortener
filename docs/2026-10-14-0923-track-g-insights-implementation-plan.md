# Track G Insights implementation plan

Date: 2026-10-14 09:23 CDT
Purpose: deliver the Track G handoff in the isolated Insights worktree after Track D merged to `origin/main`.

## Scope

- Rebase Phase 1 while preserving Track A/B/D behavior; validate migration, privacy, retention, and V1/V2/V3 consumption.
- Add workspace-scoped Insights and batched sparklines with indexed PostgreSQL reads.
- Add the authorized responsive Insights page, local charts, accessible summaries, selected-workspace scope, dashboard sparklines, and live refresh.
- Validate with unit, integration, component, local hosts, and disposable Compose stack `schwab-trackg-check` on `10.251.48.0/24`.
- Update ADR, README, and dated validation record. Report Track C and F follow-ups.

## Fences

Do not edit `infra/**`, `.github/**`, private environment files, databases or containers belonging to other projects. Preserve API keys, passwords, local files, and the canonical checkout. No push or fast-forward of `main` until Michael confirms Track C deployed `visitor-hash-secret` and the API secret reference; request his approval at that point.

## Acceptance

Track G handoff completion criteria in `docs/2026-10-14-0835-track-g-link-insights-handoff.md`, with exact test, migration, service, privacy, and UI evidence recorded in the validation note. Unfinished items remain explicit in that note.

## Outcome

Local implementation and disposable-stack validation complete on 2026-10-14. See `docs/2026-10-14-0952-track-g-link-insights-validation.md` for the exact checks, timings, and remaining Azure/browser gates. The branch is committed locally; no main push is requested before Michael confirms Track C deployed the Key Vault secret and API reference.
