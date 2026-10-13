# Accounts foundation migration validation — 2026-10-13 07:56 CDT

## Purpose and source

Record the local evidence for the first personal and organization account ownership schema. Work was done only in `/Users/michaelinman/Repos/SchwabUrlShortener-accounts-foundation` on `codex/accounts-foundation`. The implementation and guarded migration test were committed as `bdb3a1c`, after the dedicated branch's documentation commits. The original six draft schema files were preserved and completed, not reset. A copy of their pre-edit state was saved under `/private/tmp/schwab-accounts-foundation-schema-wip-20261013-01a13534.tar.gz`.

## Schema and migration choices

- Added application users with immutable IDs, issuer-plus-subject external identities, personal/team workspaces, explicit team memberships, and verifier-hash-only invitations. Email and display name are contact/profile fields, not authorization keys.
- Added nullable `url_mappings.owner_workspace_id` while retaining `tenant_id`, current tenant-scoped repository queries, guest behavior, and Slice 5 event v1. Existing API writes may continue to leave the new column null.
- The migration creates one deterministic legacy team workspace for each distinct managed Entra tenant and backfills managed mappings in the migration transaction. It compares tenant/workspace and mapping/assignment counts and fails with count-only diagnostics on mismatch. Legacy workspaces may have no application-user memberships during this transition.
- Database constraints cover identity uniqueness, personal-workspace owner uniqueness, at most one team owner, team-only memberships/invitations, role values, invitation verifier format and uniqueness, expiry ordering, redemption-state consistency, no redemption after the stored expiry, and no clearing or changing a redemption/revocation once recorded. A mapping cannot point at another legacy tenant's workspace; guests cannot have a workspace owner. Workspace kind and legacy tenant binding are immutable.
- Time passing does not itself change an invitation row. The later invitation API must reject an expired or already redeemed invitation and atomically record redemption. Current tenant authorization remains authoritative until a separately verified workspace-authorization cutover. Managed links created by the old API after this one-time backfill may still have null `owner_workspace_id`; the later cutover needs a catch-up backfill and write-path migration.

## Local validation

1. The user ran the focused Infrastructure build before and after the first migration edit; both succeeded. I restored only the solution's declared packages and ran `dotnet build SchwabUrlShortener.slnx --no-restore`: 0 warnings, 0 errors. A first local test-project build attempt could not write `bin/obj` under the tool sandbox; the next `--no-restore` attempt found missing test assets. Restoring the declared packages resolved that setup issue, and the focused test project then built with 0 warnings and 0 errors.
2. The user confirmed the pre-existing migrations were applied and `20261013121515_AccountOwnershipFoundation` was pending in `Schwab_test_local` on loopback port 63651. No migration was applied to `Schwab_local`.
3. The focused real-PostgreSQL migration test passed 1/1 with 0 skipped. It created a random temporary schema inside guarded `Schwab_test_local`, migrated from the prior Slice 5 schema, seeded two managed tenants and one guest, and migrated forward. It checked before/after counts of 3 mappings, 2 outbox rows (pending and published), 1 processed event, and 1 dead-letter row; compared all original mapping fields and full analytics rows; checked two correct legacy workspaces, guest null ownership, constraint failures, and a no-change rerun of pending migrations. Its temporary schema was dropped in cleanup.
4. The user applied the migration to the disposable `Schwab_test_local` database only. EF reported `Applying migration '20261013121515_AccountOwnershipFoundation'` and `Done.` No application database or deployment was migrated.
5. The user ran `dotnet test SchwabUrlShortener.slnx --no-restore` against the dedicated Schwab PostgreSQL, Redis, and Kafka test endpoints at loopback ports 63651, 63652, and 63653. Result: **57/57 passed, 0 failed, 0 skipped**. This includes the existing API/worker integration paths after the new schema was applied to the test database. The integration suite clears only the guarded disposable `Schwab_test_local` tables and uses its own Redis namespaces and Kafka test topics/groups.
6. Staged paths were limited to the three original modified schema files, three original new schema/migration files, and the new migration test. `git diff --cached --check` was clean before the implementation commit.

## Recovery and rollout gate

The migration's `Down` refuses to drop account tables or `owner_workspace_id`, because account data written after deployment would be lost. Before any non-test rollout, take a verified backup, rehearse a restore, inspect counts and tenant ownership, and use a reviewed forward repair if a migrated database needs correction. Application-image rollback and database recovery are separate operations. This note does not authorize migration of `Schwab_local` or production data.

## Remaining work and evidence limits

- Schema presence does not provide workspace authorization, signup, Google federation, Entra External ID, account linking, invitation acceptance, or a workspace switcher. Those require later API/UI slices and explicit decisions in the account plan.
- Live Entra wrong-scope rejection, second-tenant isolation with real identities, actual seven-day guest expiry, and provider sign-in were not verified here. Test-handler authentication and local real-service tests are not live-provider evidence.
- No application database, unrelated Docker container, AIF service, secret, canonical checkout, remote branch, PR, or deployment was changed for this accounts implementation. The only database write outside temporary test schemas was the explicitly targeted disposable `Schwab_test_local` migration and its integration test data.
- The accounts branch has not been pushed or integrated. Review the final Git status and commit before any later publication decision.
