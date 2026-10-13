# Local PostgreSQL name cleanup — 2026-10-12 23:19 CDT

## Purpose

Remove `slice2` from active local PostgreSQL names while preserving the existing application data, password, and historical documentation. The user requested one small step at a time, with a pause for the result after each command.

## Scope and sequence

1. Restrict the integration-test safety guard to the requested disposable database name `Schwab_test_local`; keep the application database blocked.
2. Rename the existing empty local test database in place, then confirm the result before configuring or running tests.
3. Inventory the remaining local PostgreSQL names and their dependencies before changing the application database, role, container, volume, or credential file. Back up credential-bearing configuration before any edit. Preserve the existing password and application data.
4. Run the test suite against the isolated test database after migration; verify the application still connects to its renamed database after any later configuration changes.

## Acceptance criteria

- Active test code and local PostgreSQL names use the agreed names without `slice2`.
- The application database and credentials remain intact and accessible.
- A full test run targets only the disposable test database; no production deployment is claimed.
- The user receives one command at a time and confirms each result before the next dependent step.

## Current status

- The test guard accepts only `Schwab_test_local`; its focused scope test passed (1/1) using a database-free test path. Commit is pending.
- The existing database is still named `schwab_slice2_test_local`. No database rename has been run yet.
- The current application database is `schwab_slice2_local`. No production database is provisioned or named in this repository.
