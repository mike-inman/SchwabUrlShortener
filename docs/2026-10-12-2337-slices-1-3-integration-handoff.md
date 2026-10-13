## Slices 1–3 Integration Handoff
Status: drafted on 2026-10-12
You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, HEAD, `origin/main`, `git status --short --branch`, and attached worktrees before edits. Preserve all pre-existing work.
- Start from the committed handoff on local `main` and create a dedicated integration branch, `codex/integrate-slices-1-3`, in this checkout. Do not merge into `main` or rewrite, reset, delete, or rebase the source branches.
- Preserve all API keys, passwords, user-secrets stores, `.env*`, local agent configuration, the unpublished assignment PDF, Docker data, and unrelated files. Never print or commit secret values. Back up any credential-bearing file before an authorized edit.
- Keep changes within the scope below. Use narrow validation after the first substantive merge or conflict resolution. Review staged paths and diffs before every commit.

Task: Integrate the committed Slice 1 security, Slice 2 PostgreSQL API, and Slice 3 Blazor Web work into one reviewable branch.

Goal:
Produce one branch that builds and tests as a whole, preserves the anonymous guest and Entra-protected managed flows, and records exactly what was verified locally versus what remains unverified.

Important context:
- At drafting, pre-handoff local `main` is `503ff11ed66a9198917e3523c901eadb7bbb9dfe`, one commit ahead of `origin/main` (`e3325127a5630ba7f5e5562c5bbfcdc3a98c8ee3`). Committing this document will advance `main` by one document-only commit; start from that commit. Source tips are Slice 1 `805f2430189a40d4aa019368f1d80c46509fd71c`, Slice 2 `5137915c0108f69ea3acf35a89442ba9b6ffde3a`, and Slice 3 `954c6aa7f6e1ab14f37b7630ba5d676a22cae768`. Confirm these before integrating; stop for scope review if a source tip has moved or the checkout has unrelated changes.
- The three source branches have separate worktrees. Slice 2 and Slice 3 both descend from Slice 1; local `main` and Slice 1 diverged at `e332512`. Therefore merge conflicts are plausible in `README.md`, docs, `src/Shortener.Api/Program.cs`, and tests. Resolve by reviewing both sides and their intended behavior; never accept an entire side by default.
- Slice 2 has a real PostgreSQL repository, migration, readiness checks, and an API policy requiring delegated `access_as_user` for managed endpoints. Its integration-test guard permits only the disposable database `Schwab_test_local`. Slice 3 has a Blazor Interactive Server UI that signs in with Microsoft.Identity.Web, acquires the API token server-side, and calls the API over HTTP. Guest creation and redirects remain anonymous.
- Separate-worktree local verification succeeded: home-tenant Entra sign-in, managed create/list/detail/redirect count, anonymous guest create/redirect, API readiness, and Slice 2 full tests (19 unit, 9 integration) against PostgreSQL. Slice 3's prior isolated Web tests passed (11). Those results do not establish that the merged branch has been built or tested.
- Current local PostgreSQL uses container `schwab-url-shortener-postgres`, role `schwab_local`, databases `Schwab_local` and `Schwab_test_local`, and loopback port `63651`. The password is in a restricted local file; do not display, copy into Git, or replace it. The Web and API user-secrets stores are separate and already configured locally. No production database, deployment, or tenant allowlist has been established.

In scope only:
1. Create `codex/integrate-slices-1-3` from the committed local `main`, then integrate the exact Slice 1, Slice 2, and Slice 3 branch tips in that order, preserving their histories and resolving conflicts deliberately.
2. Reconcile shared API, repository, tests, `README.md`, solution membership, and dated docs only where necessary to produce a coherent combined application. Keep the PostgreSQL source of truth and the Slice 2 delegated-scope policy; keep the Web's server-side token acquisition and HTTP-only API boundary.
3. Fix integration-specific build/test/runtime defects revealed by the combined branch. Keep fixes minimal and add or adjust focused tests only when a genuine integration behavior needs coverage.
4. Create a dated integration validation note in `docs/` that records merge inputs, conflicts/resolutions, exact checks, local live observations, remaining gaps, and the resulting integration commit. Commit only intended tracked changes on the integration branch.

Out of scope:
- Redis caching, Kafka analytics, Docker Compose, deployment, production credentials or database provisioning, branch pushes, PR creation, and merging the integration branch into `main`.
- New app roles, tenant allowlists, per-user `oid` ownership, edit/delete routes, or a revised authentication architecture. Record such gaps instead of silently expanding the contract.
- Any deletion, rotation, or disclosure of local passwords, API keys, user secrets, databases, containers, volumes, or backups. Do not change credentials to make tests pass.
- Changes in Slice 1, Slice 2, or Slice 3 worktrees or source branch histories.

Required discovery before edits:
- Read `AGENTS.md`, this handoff, `docs/2026-10-12-1953-production-ready-slices-and-handoffs.md`, each slice's implementation/validation note, `docs/2026-10-12-2304-local-entra-postgresql-validation.md`, and `docs/2026-10-12-2319-local-postgresql-name-cleanup-plan.md` from the relevant branches.
- Inspect `git merge-base` and changed paths for `main` versus each source branch; inspect the current `src/Shortener.Api/Program.cs`, `src/Shortener.Infrastructure/**`, `src/Shortener.Web/**`, `tests/**`, `SchwabUrlShortener.slnx`, and README before resolving conflicts.
- Confirm the intended API scope, tenant claim use, managed versus guest route protection, and Web-to-API contracts in source. Check local test-database isolation before running any test that truncates tables.

Implementation requirements:
- Preserve anonymous guest creation and public redirects. Managed create/list/detail/analytics must require a validated Entra token with the delegated `access_as_user` scope and derive the tenant from validated claims, never caller input.
- Keep managed data tenant-scoped in PostgreSQL; preserve global short-code uniqueness, seven-day guest expiry, and managed redirect analytics. Do not weaken authorization or switch to an in-memory runtime repository during conflict resolution.
- Keep access tokens and client credentials on the server. Preserve the committed `UserSecretsId` declarations, local secret-store identities, and API/Web configuration key names without copying values into tracked files.
- If integration reveals a contract mismatch, make the smallest coherent fix and document why. Preserve source-branch work and establish a new integration commit rather than rewriting history.

Exception / observability requirements:
- Keep API and Web failures inspectable through structured, sanitized logs and safe caller messages. Do not log tokens, cookies, authorization headers, connection strings, secret values, or raw provider payloads. Preserve useful correlation and exception context when fixing integration failures.

Realtime / no-polling requirements:
- Not applicable: the integrated app's link operations are synchronous and no operator-facing background job is introduced. The managed dashboard refreshes on user action; do not add polling or a new realtime transport as part of this handoff.

Testing:
- After each merge/conflict resolution, run the narrowest affected build/test first. Then run `dotnet build SchwabUrlShortener.slnx --nologo` and `dotnet test SchwabUrlShortener.slnx --nologo` on the integrated branch.
- Full integration tests may clear `url_mappings` and must receive a connection string for `Schwab_test_local` only. Verify the database name without displaying the password or running `dotnet user-secrets list`. Never target `Schwab_local` with the integration test suite.
- When the local services are available, verify `/health/ready`, an anonymous guest create/redirect, and a signed-in managed list/create/detail/redirect-count flow on the **integrated** branch. Browser sign-in may require user interaction; record any unperformed step rather than claiming it passed. Avoid clashing with the already running API/Web ports.
- Keep second-tenant isolation, a live wrong-scope token rejection, guest expiry at the live seven-day boundary, and production readiness explicitly unverified unless actually exercised. Isolated tests are not live Entra evidence.
- Run `bash scripts/check-codex-handoff.sh docs/2026-10-12-2337-slices-1-3-integration-handoff.md` and review the final intended-path diff/status before committing. Do not publish or deploy.

Documentation:
- Date the integration validation note and keep it in this project's `docs/` directory. Record source SHAs, conflict choices, tests with pass/fail counts, local service evidence, and remaining risks. Distinguish configured, tested, committed, pushed, and deployed states.
- Report the integration branch and final commit, files changed, test results, any live-test blocker, and the exact next handoff gate. Do not claim Slice 4 is ready until the integrated PostgreSQL path passes.

Completion criteria:
- [ ] A dedicated integration branch contains all three source slices and preserves their security and data behavior without modifying their worktrees.
- [ ] The combined solution builds and its unit, Web, and PostgreSQL integration tests pass against `Schwab_test_local`, or a precise blocker is recorded without weakening tests.
- [ ] The integrated API and Web path is locally smoke-tested where user sign-in is available, with any unverified live claims clearly marked.
- [ ] A dated integration validation note and reviewed commits are present; `main`, source branches, credentials, and local databases remain protected.
