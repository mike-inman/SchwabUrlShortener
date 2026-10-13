## Personal and Organization Accounts Foundation Handoff
Status: drafted on 2026-10-13
Drafted at 06:36 America/Chicago. Purpose: implement the first ownership-schema stage of `docs/2026-10-13-0541-personal-and-organization-accounts-plan.md` after Slice 5 integration.
You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:
- Read `AGENTS.md`; confirm `pwd -P`, branch, commit, `origin/main`, worktree list, and `git status --short --branch` before edits. Treat modified and untracked files as user-owned.
- Preserve `.env*`, local user-secrets, API keys, passwords, agent configuration, databases, containers, the unpublished assignment PDF, and unrelated changes. Never print secret values or run `dotnet user-secrets list`.
- The clean canonical tip when this handoff was drafted was `6fadd511d7e7d72cd678cb43b7abb50e11ad8bc9` on `codex/integrate-slices-1-3`; Slices 1–5 are locally integrated. Commit this handoff before creating a dedicated `/Users/michaelinman/Repos/SchwabUrlShortener-accounts-foundation` worktree on `codex/accounts-foundation` from that exact committed handoff tip. Verify ancestry and clean state. Do all implementation in that worktree, not canonical or earlier slice worktrees.
- Use relevant available MCP servers only where they help, verify results against the repository, and keep secrets out of tool output. Use narrow checks first and commit only intended paths.
- The older `docs/2026-10-12-1953-slice-6-compose-observability-handoff.md` remains a separate infrastructure track; this account work does not replace or implement it.

Task: Add a backward-compatible PostgreSQL foundation for personal and organization workspaces while preserving the current Entra tenant-owned managed-link behavior and Slice 5 analytics.

Goal:
Represent application users, external login identities, personal/team workspaces, memberships, and invitations; backfill existing managed links into one legacy team workspace per Entra tenant without losing links, counts, or click history. Keep the current app behavior functional until a later authorization slice uses the new ownership model.

Important context:
- The account architecture and staged sequence are in `docs/2026-10-13-0541-personal-and-organization-accounts-plan.md`. Its statement that Slice 5 is still being developed is historical: Slice 5 was merged at `79869a0` and locally validated at 56/56 tests; see `docs/2026-10-13-0628-slice-5-canonical-integration-validation.md`.
- Current managed ownership uses validated Entra `tid` in `url_mappings.tenant_id`; guests have no owner and expire after exactly seven days. Current API delegated scope is `access_as_user`. Do not treat email, domain, or a customer identity tenant's `tid` as an individual or team authorization key.
- Slice 5's `ManagedClickEventV1`, `click_outbox`, `processed_click_events`, and consumer aggregate writes still use tenant ID. This foundation must preserve that event version and all existing analytics behavior. A later API/event migration will introduce workspace-aware authorization and versioned analytics changes.
- The user wants both personal accounts and organization teams, with eventual email registration and Google sign-in. This stage lays out data ownership only; it does not configure an identity provider or deliver live sign-up.
- Planning assumptions from the approved account plan are owner/admin/member roles, invitation-only team membership, multiple-team membership, and team creation by a signed-in user. Record any policy decisions that cannot be safely implemented from these assumptions rather than silently expanding scope.
- Live Entra wrong-scope rejection, second-tenant isolation, actual seven-day guest expiry, deployment, and application-database migration remain unverified/out of scope for this handoff.

In scope only:
1. Add explicit EF Core/PostgreSQL migrations and model mappings for application users, external identities keyed by validated issuer plus stable provider subject, workspaces, memberships with constrained roles, and invitation records that support expiry and one-time redemption in a later API slice. Keep passwords, OAuth secrets, access tokens, and raw invitation tokens out of these tables.
2. Add a nullable `owner_workspace_id` to managed mappings for a staged transition. Backfill existing managed mappings to one legacy team workspace per distinct non-null Entra `tenant_id`; leave guest ownership null. Retain `tenant_id` and current indexes/constraints required by deployed code and Slice 5 analytics. Ensure migration can run once on a populated database without changing links or counts.
3. Add focused database migration/backfill tests for at least two tenants, guests, existing counts, outbox and processed events, uniqueness/foreign keys, membership roles, identity keys, invite single-use/expiry data rules, and rerunning pending migrations. Document a rollback/recovery strategy before any production use.
4. Record exact schema choices, migration evidence, source commit, and remaining API/auth/UI steps in a dated `docs/` validation note. Update architecture documentation only where needed to describe the staged ownership model.

Out of scope:
- No public registration, Google federation, Entra External ID setup, login UI, workspace switcher, invitation emails, or identity linking flow.
- No change to current API authorization, Web sign-in, repository tenant queries, Redis cache behavior, Slice 5 event version or consumer, guest redirects, or production-facing route behavior.
- No production/app database migration, deployment, push, container reset, volume deletion, secret rotation, or modification of AI-Influencer-Factory services.
- No broad renaming of `tenant_id`, destructive conversion of existing ownership, or false claim that schema presence makes workspace authorization active.

Required discovery before edits:
- Inspect `AGENTS.md`, this handoff, the account plan, Slice 5 validation notes, `src/Shortener.Domain/UrlMapping.cs`, `src/Shortener.Infrastructure/ShortenerDbContext.cs`, existing migrations and model snapshot, PostgreSQL repository transaction boundaries, integration-test database guard, and current analytics event/outbox/consumer schema.
- Inspect existing `docs/2026-10-12-1953-slice-6-compose-observability-handoff.md` only to avoid scope overlap. Check all target files for pre-existing changes before editing.
- Verify the isolated `Schwab_test_local` database target before any migration test that writes data. Do not run test truncation against `Schwab_local` or any other application database.

Implementation requirements:
- Before schema edits, write a dated decision/threat-model note covering issuer/subject identity keys, explicit account linking, team roles/invitations, legacy tenant access during transition, migration recovery, and cross-workspace data exposure. Use the account plan's stated assumptions where no later user decision exists; mark choices that need later confirmation.
- Use immutable application user IDs and separate provider identity records. Uniqueness must be based on verified issuer plus stable provider subject; email/display name are mutable contact fields, not authorization or automatic account-linking keys.
- Model personal workspaces with one owner and team workspaces with explicit memberships. Enforce uniqueness and valid states with database constraints where feasible. Invitations must be scoped to a workspace, expire, and be redeemable once; persist only a verifier/hash rather than a raw bearer invitation token.
- Make legacy team-workspace creation/backfill deterministic and safe for multiple existing tenants. Legacy workspaces may temporarily have no application-user memberships while access still follows the existing validated Entra tenant path; document that transitional state. Preserve every managed mapping, guest invariant, hit count, last-access timestamp, outbox row, processed event, and dead-letter row. Provide before/after count checks and idempotent migration behavior. Do not require the current API to populate the new column yet; keep it nullable during this stage.
- Keep migrations compatible with the current app and worker throughout rollout. If adding constraints would break existing writes, stage them for the later API authorization slice and document that gate.
- Do not apply a destructive `Down` path to real data. State what a safe restore requires once new account data exists.

Exception / observability requirements:
- Fail migration with actionable, sanitized diagnostics if tenant-to-workspace backfill or constraints cannot be completed; never log connection strings, tokens, raw invitation values, or personal profile data.
- Record migration/backfill counts and mismatches in the validation report without recording secrets or personal data. Runtime observability changes are not applicable because this slice adds no new runtime flow.

Realtime / no-polling requirements:
- Not applicable: this is a persistence foundation with no operator-facing live UI. Preserve existing Blazor automatic dashboard updates and do not introduce a Refresh control or polling path.

Testing:
- Run focused migration/backfill and constraint tests against only guarded `Schwab_test_local`, using isolated services without stopping or reusing unrelated containers. Include pre-existing rows for two Entra tenants, guests, nonzero analytics counts, pending/published outbox rows, and processed events; verify row counts and referential integrity before and after migration.
- Verify the unchanged current API and Slice 5 worker still build and pass relevant focused tests. Then run solution build and full tests with real PostgreSQL, Redis, and Kafka where safely available. Distinguish test-handler authentication from live Entra validation and report exact counts, warnings, skipped tests, and any blocked checks.
- Do not add superficial tests that merely restate schema code. Test tenant separation, duplicate issuer/subject rejection, membership constraints, invitation verifier uniqueness and expiry/redemption-state constraints, and backfill preservation. Actual invitation redemption and replay handling belong to the later API slice.

Documentation:
- Keep the implementation plan and validation record dated under `docs/`, with purpose, scope, migration/recovery procedure, evidence, open decisions, and next handoff boundary. Update the account plan's historical Slice 5 status only in the implementation worktree if needed and keep the original decisions intact.
- Before committing, review staged paths and diff; commit only the foundation files and dated notes. Report local test results separately from integration, live identity, deployment, and operational acceptance.

Completion criteria:
- [ ] The schema represents personal users/workspaces and explicit organization memberships without using email or external tenant ID as an individual authorization key.
- [ ] Existing managed mappings are backfilled into one legacy team workspace per Entra tenant; guest mappings remain unowned; link data, counts, and Slice 5 history are preserved.
- [ ] Current Entra API/Web and Kafka analytics behavior remain compatible before any workspace-aware authorization is enabled.
- [ ] Guarded real-PostgreSQL tests cover populated migration, isolation/constraint behavior, and preservation of existing data; full solution checks and limitations are reported accurately.
- [ ] A dated validation note and rollback/recovery plan exist, intended changes are committed in the dedicated worktree, and no app database, secrets, unrelated containers, or deployment were changed.
