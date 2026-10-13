# Web session recovery plan — 2026-10-13 00:31 America/Chicago

## Purpose

Restore managed-link access gracefully when the Web process restarts while a browser still has a valid sign-in cookie. The in-memory Microsoft.Identity.Web token cache is cleared at restart; subsequent API token acquisition returned `MsalUiRequiredException` with `user_null` during local testing.

## Scope and approach

- Keep the current Entra registrations, scopes, API authorization, local credentials, and in-memory cache unchanged.
- Recognize the Microsoft.Identity.Web user-interaction challenge separately from other token errors.
- Send the browser through one new Entra authorization challenge, then back to the managed dashboard. Preserve a marker in the return URL so a failed renewal cannot loop indefinitely.
- Remove the marker after successful access is restored so a later renewal can happen normally.
- Stop live API retries when renewed sign-in is required. If the single renewal fails, show a clear sign-in action.

## Acceptance criteria

- A stale browser cookie after Web restart initiates one renewal and returns to the dashboard when Entra issues an API token.
- A failed renewal does not loop or keep polling the API every five seconds.
- Existing authentication, tenant isolation, and secret storage are preserved.

## Local verification and remaining work

- `dotnet build src/Shortener.Web/Shortener.Web.csproj --no-restore --disable-build-servers`: passed with zero warnings and errors.
- `dotnet build SchwabUrlShortener.slnx --no-restore --disable-build-servers`: passed with zero warnings and errors.
- `dotnet test tests/Shortener.WebTests/Shortener.WebTests.csproj --no-restore --disable-build-servers`: 17 passed, 0 failed.
- The browser recovery flow has not yet been retested against live Entra after restarting the Web process.
- This remains an in-memory token cache. A Web restart requires an Entra authorization round trip; a persistent distributed token cache would be a separate design and security change.
