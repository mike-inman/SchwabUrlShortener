# Slices 1–3 integration validation — 2026-10-12 23:42 CDT

## Purpose and plan

Integrate the committed security, PostgreSQL API, and Blazor Web slices on `codex/integrate-slices-1-3` in `/Users/michaelinman/Repos/SchwabUrlShortener`. Preserve their histories, unrelated work, credentials, databases, and source worktrees. Accept when the combined solution builds, all tests pass against only `Schwab_test_local`, local anonymous behavior works, and live gaps are recorded.

## Inputs and conflict decisions

- Started from clean local `main` `516f9b4c008c992c5d95abfe902bb4ead470dcee`; `origin/main` was `e3325127a5630ba7f5e5562c5bbfcdc3a98c8ee3`.
- Merged Slice 1 `805f2430189a40d4aa019368f1d80c46509fd71c`, Slice 2 `5137915c0108f69ea3acf35a89442ba9b6ffde3a`, and Slice 3 `954c6aa7f6e1ab14f37b7630ba5d676a22cae768` in that order. Merge commits: `4723247`, `a1400cb`, `ca8d6bd`.
- Slice 1 conflicts in README, API, Domain/Application/Infrastructure source, and tests came from the canonical starter and the Slice 1 implementation both adding the same baseline paths. Reviewed their diff and kept the implemented guest/managed contracts and tests. Kept the canonical architecture document's superseded-status line, which points to the later production-minded plan.
- Slice 2 and Slice 3 each conflicted only in their own handoff document. Kept the respective branch's worktree-specific, dated handoff so its historical scope and ownership remain accurate. Their code merged without conflict. The final README now describes the combined PostgreSQL API and Web setup.
- No runtime integration fix was needed. Normal API registration remains PostgreSQL-only; managed routes require the delegated `access_as_user` scope and derive `tid` from the validated principal. Web token acquisition stays server-side and its API client uses HTTP. No source branch or worktree was changed.

## Checks on the integration branch

- Narrow post-merge builds: API after Slices 1 and 2, Web after Slice 3; all passed with zero warnings and errors.
- `dotnet build SchwabUrlShortener.slnx --nologo`: passed, zero warnings and errors.
- `dotnet test SchwabUrlShortener.slnx --nologo`: passed against migrated local PostgreSQL 16 database `Schwab_test_local` on loopback port 63651: 19 unit, 9 real PostgreSQL/API integration, and 11 Web tests; zero failures or skips. The connection was assembled in process from the existing restricted local API secret store with only its database field changed from `Schwab_local` to `Schwab_test_local`; no value was printed, written, or changed in that store. The test guard separately requires that exact disposable database before truncating `url_mappings`.
- `bash scripts/check-codex-handoff.sh docs/2026-10-12-2337-slices-1-3-integration-handoff.md`: passed.

## Local integrated service observations

- Started the integrated API on `https://localhost:7181`, leaving existing ports 7100 and 7130 untouched. `/health/ready` returned 200 against `Schwab_local`; anonymous guest creation returned 201 with an expiry, and `/r/{code}` returned 302 to the submitted destination. An unauthenticated managed list returned 401. This guest creation added one local application-database row; it was not deleted.
- Started the integrated Web app on `https://localhost:7182` pointing at the integrated API. Its guest page returned 200 with the seven-day notice; unauthenticated `/dashboard` returned 302 to sign-in. The Web guest form was not interactively submitted: no browser automation surface was available. The signed-in managed Web/API flow was not repeated on the integrated branch because this separate Web port has no verified Entra callback registration and no interactive browser was available. The prior cross-worktree home-tenant sign-in and managed create/list/detail/redirect-count observation in `2026-10-12-2304-local-entra-postgresql-validation.md` is historical evidence only.

## Remaining gate

Interactive integrated Web guest submission and home-tenant managed sign-in/list/create/detail/redirect-count remain unverified. Live wrong-scope rejection, second-tenant isolation, and guest expiry at the live seven-day boundary remain unverified. Production database, deployment, tenant allowlist, and operational acceptance remain unverified. Slice 4 is gated on review of this integrated PostgreSQL path and completion or explicit acceptance of the live integrated managed-flow gap. This branch is local and has not been pushed or merged into `main`.
