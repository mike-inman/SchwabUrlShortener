# Slice 1 baseline review and parallel preparation

Date: 2026-10-12 20:49 CDT
Purpose: Review the Slice 1 security contract, commit a verified common baseline, and prepare separate Slice 2 and Slice 3 worktrees without implementing either slice.

## Plan

- Review all Slice 1 tracked diffs and copied untracked source against the handoff, including exact guest expiry, redirect analytics, tenant isolation, authorization, and global code uniqueness.
- Fix material Slice 1 defects only. Run targeted build/tests followed by solution build/tests.
- Stage only the reviewed Slice 1 baseline and this dated review note. Inspect staged paths and diff, then commit on the Slice 1 branch.
- Create Slice 2 and Slice 3 worktrees from the exact reviewed commit. Copy each existing dated handoff, adapt actual path and branch, define disjoint file ownership, run the handoff checker, and inspect real PostgreSQL availability.

## Acceptance criteria

- The reviewed Slice 1 commit has no secrets, assignment PDF, or unrelated canonical-checkout files.
- Both new worktrees point to the same Slice 1 commit and have distinct branches.
- Each handoff names its worktree, branch, ownership, checks, and remaining live-service prerequisites.
- No Slice 2 or Slice 3 implementation begins in this preparation step.

## Review and outcome

- Confirmed the source and handoff baseline in this isolated worktree. The canonical checkout remains at `e3325127a5630ba7f5e5562c5bbfcdc3a98c8ee3` with its pre-existing dirty state untouched.
- Reviewed the copied source, tracked diff, API routes, configuration, and tests against the Slice 1 handoff. Domain expiry uses `now >= CreatedAtUtc.AddDays(7)`; the domain test checks just before and at the boundary, and the API test checks a redirect at creation and a 404 at expiry. Guest redirects never invoke hit recording. Managed lookup and analytics require authorization and use a nonempty tenant GUID from the authenticated principal. The repository uses an atomic, case-insensitive short-code insertion across guest and managed modes. API tests cover anonymous redirect, tenant isolation, unauthorized requests, invalid tenant context, and cross-mode collision.
- Fixed one material error-path defect: duplicate custom codes now throw `ShortCodeConflictException`, and the API maps only that exception to 409. Other storage/application failures can reach the safe 500 handler rather than being mislabeled as a client conflict.
- Updated the handoff checker to validate a declared path against registered repository worktrees, because its prior canonical-path-only rule could not check actual Slice 2/3 worktree paths. Its self-test passed.
- Targeted unit tests passed (19); targeted API integration tests passed (3). `dotnet build SchwabUrlShortener.slnx --nologo` passed with no warnings or errors; `dotnet test SchwabUrlShortener.slnx --nologo` passed (19 unit, 3 integration). Tests use a test-only authentication handler. Live Entra JWT issuer, audience, and signature validation remains unverified without an app registration, tenant, and access token.
- Docker server 29.4.3 is available, `postgres:16-alpine` is locally present, and an isolated disposable PostgreSQL 16 container answered `SELECT 1`. That probe was stopped and removed. Slice 2 can create its own disposable real-database test instance; no existing database or credential was touched.
- No Slice 2 or Slice 3 implementation was started. Worktree creation and handoff checks follow the reviewed baseline commit.
