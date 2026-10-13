# Accounts foundation resume handoff — 2026-10-13 07:16 CDT

## Start here

Work only in `/Users/michaelinman/Repos/SchwabUrlShortener-accounts-foundation` on `codex/accounts-foundation`. Read `AGENTS.md`, `docs/2026-10-13-0636-personal-organization-accounts-foundation-handoff.md`, `docs/2026-10-13-0541-personal-and-organization-accounts-plan.md`, and `docs/2026-10-13-0648-accounts-foundation-decisions-and-threat-model.md`. Inspect branch, commit, `origin/main`, worktree list, and status before edits. Preserve every current modified and untracked file. The canonical checkout and draft PR #1 are separate; do not move this incomplete implementation into them.

## Exact state at pause

- The decision/threat-model note was committed at `f5537ec` in the dedicated accounts worktree. Before this WIP, the worktree began at canonical handoff commit `3fc8fcb`. `origin/main` was `624d391` at the last check.
- Uncommitted intended changes: `src/Shortener.Domain/UrlMapping.cs`, `src/Shortener.Infrastructure/ShortenerDbContext.cs`, `src/Shortener.Infrastructure/Migrations/ShortenerDbContextModelSnapshot.cs`, new `src/Shortener.Infrastructure/AccountFoundationModels.cs`, and generated `src/Shortener.Infrastructure/Migrations/20261013121515_AccountOwnershipFoundation.cs` plus its `.Designer.cs`.
- The model has application users, issuer/subject external identities, personal/team workspaces, memberships, invitation verifier hashes, and nullable mapping `owner_workspace_id`. EF generated migration and snapshot. These are an **unfinished draft** and must be reviewed and corrected before use.
- I checked `git diff --check` for the tracked changes; it returned clean. The focused Infrastructure build passed with 0 warnings and 0 errors **before generating the migration**. The migration has not been built or applied, and no migration/backfill tests have run.
- Package restore used only the projects' already declared dependencies. No database, Docker container, secrets, canonical branch, or deployment was changed.

## Critical unfinished work

1. Review the generated migration. It creates tables and the nullable mapping column but **does not yet create one legacy team workspace per existing Entra tenant or backfill `owner_workspace_id`**. Add deterministic, transaction-safe backfill and sanitized mismatch failure. Keep new API writes compatible while the column is nullable.
2. The generated `Down` drops the new account tables and column, which would destroy later account data. Replace with a non-destructive recovery policy or an explicit guard that refuses unsafe rollback; document backup/restore and forward repair.
3. Review schema constraints against the handoff, especially membership scope, owner uniqueness, invitation one-time/expiry state, and the fact that time-based expiry requires later runtime enforcement. Preserve tenant queries and Slice 5 event v1.
4. Add guarded real-PostgreSQL migration tests using only `Schwab_test_local`, with two managed tenants, guests, nonzero counts, outbox and processed events, constraints, and rerun behavior. Never run truncation or migrations on `Schwab_local` or an application database. Avoid touching AI-Influencer-Factory containers.
5. Build the solution and run focused then full guarded tests. Record exact checks and failures in a dated validation note. Review staged paths/diff and commit only completed intended changes.

## Evidence boundary

Previous canonical Slice 5 local tests were user reported as 56/56 after integration. Those results do not validate this account schema. No live Entra wrong-scope or second-tenant check, real seven-day expiry, app-database migration, PR merge, push of the accounts branch, deployment, or operational acceptance is claimed here.

## Interaction preference

Guide the user one small step at a time and wait for their result before giving another command. Keep plans and validation under dated `docs/` filenames. Never print secret values or run `dotnet user-secrets list`.
