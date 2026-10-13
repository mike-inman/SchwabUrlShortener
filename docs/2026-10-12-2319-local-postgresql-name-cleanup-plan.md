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

## Outcome at 2026-10-12 23:31 CDT

- The test guard now accepts only `Schwab_test_local`; the focused scope test passed (1/1), and the guard/documentation change was committed as `018d1fa`.
- The former test and application databases were renamed to `Schwab_test_local` and `Schwab_local`. Before cutover, both databases were backed up privately under `~/.config/schwab-url-shortener/backups/`.
- A new PostgreSQL 16 container `schwab-url-shortener-postgres` uses volume `schwab-url-shortener-pgdata`, role `schwab_local`, and loopback port `63651`. The saved password was copied to restricted `~/.config/schwab-url-shortener/postgres.env`; its value was neither displayed nor changed. The old password file was moved to a restricted, cleanly named backup in the same backup directory.
- The backed-up application and test databases were restored to the new volume. The application table had 2 rows before and after restore. The saved password was verified over TCP with the new role. The API user-secrets file was backed up before each change, then updated only for database name, role, and port.
- Automatic approval review rejected a proposed in-place role rename because its temporary passwordless administrator could remain after a failure. That operation did not run; the separate, password-protected container and restore path was used instead.
- The API restarted and `/health/ready` returned `Healthy` after the old container was stopped. The existing migration was applied to `Schwab_test_local`; the full solution tests passed: 19 unit and 9 integration tests. This does not prove a live second-tenant or production flow.
- A full private archive of the stopped original volume was saved as `~/.config/schwab-url-shortener/backups/postgres-volume-before-name-cleanup-2026-10-12.tar`; its listing contained 1,608 entries. The archive has not been restore-tested. The old stopped container was removed.
- Automatic approval review rejected deletion of the obsolete Docker volume `schwab-url-shortener-slice2-pgdata` because that would destroy the original volume. It remains intact pending explicit user authorization. The active container, role, databases, volume, and credential filename have clean names.
- No production database is provisioned or named in this repository; no deployment or operational acceptance is claimed.
