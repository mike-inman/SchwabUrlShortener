# Entra delegated-scope enforcement — 2026-10-12 22:20 CDT

## Purpose and scope

Close the managed API authorization gap found during local Entra setup. Preserve anonymous guest creation and redirects. Keep the change in the Slice 2 API and its integration tests; do not change the Web client or any credential store.

## Plan and acceptance criteria

1. Require an authenticated access token with the delegated `access_as_user` scope for all managed API endpoints.
2. Verify that a test identity with a missing or unrelated scope receives 403 while existing authorized test identities retain access.
3. Build the solution and record exactly what was tested. Keep live Entra and PostgreSQL end-to-end verification open until exercised.

## Outcome

- The default authorization policy now requires authentication and `access_as_user`; all managed endpoints use that policy through `RequireAuthorization()`. Anonymous endpoints retain `AllowAnonymous()`.
- The test-only authentication handler supplies an `scp` claim to authorized clients. A focused regression test verifies 403 for missing and unrelated scopes on managed routes.
- `dotnet build SchwabUrlShortener.slnx --no-restore --nologo`: passed with zero warnings and zero errors.
- Focused `ManagedEndpointsRequireDelegatedScope` test: passed using a test identity. It did not contact Entra or PostgreSQL.
- `dotnet test tests/Shortener.UnitTests/Shortener.UnitTests.csproj --no-restore --nologo`: 19 passed.

## Remaining verification

- Run the full solution test suite against a dedicated disposable PostgreSQL database. Existing integration tests require a database named `schwab_slice2_test_*` and truncate its `url_mappings` table.
- Configure a separate local API database, apply migrations, and run the API and Web app with a real Entra sign-in. Confirm token acquisition, delegated scope enforcement, tenant ownership, and the managed request path.
- No deployment or operational acceptance is claimed.
