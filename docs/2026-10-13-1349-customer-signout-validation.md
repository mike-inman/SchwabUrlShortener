# Customer sign-out validation — 2026-10-13 13:49 CDT

## Change

- The navigation renders statically for each HTTP request. The workspace selector is a separate interactive component and still appears only when the customer has two or more workspaces.
- Authenticated Web responses send `Cache-Control: private, no-store` so browser history cannot reuse a cached dashboard after sign-out.
- Customer OIDC sign-out sends `logout_hint` from the `login_hint` claim when available, without logging it. The registered `/signout-callback-oidc` remains unchanged, and its completion redirects to the anonymous branded `/signed-out` page.
- The Web uses the cookie scheme to authenticate the HTTP request, while OIDC remains the sign-in and sign-out challenge scheme.

## Findings and checks

- Before the change, a fresh anonymous live GET `/` rendered Sign in and no Sign out; the response already had `no-cache, no-store, max-age=0`. No authenticated browser session was connected, so the original live sign-out POST and its cookie headers could not be inspected directly.
- An authenticated in-process HTTP test signs in with a chunked cookie, confirms the nav renders Sign out and sends `no-store`, POSTs the actual antiforgery-protected `/account/signout`, and verifies that the response expires the base cookie and every chunk. The OIDC redirect contains `logout_hint` when the claim is present and keeps `/signout-callback-oidc`. A fresh anonymous GET `/` renders Sign in without Sign out; `/signed-out` is anonymous and branded. A separate test verifies that an absent hint adds no parameter.
- `dotnet build SchwabUrlShortener.slnx --no-restore -warnaserror`: 0 warnings, 0 errors.
- Full `dotnet test SchwabUrlShortener.slnx` in disposable containers: 25 unit, 34 Web, and 20 integration tests passed; 79 passed, 0 failed, 0 skipped. PostgreSQL, Redis, and Kafka were disposable; the test database was migrated separately. The disposable containers were removed after the run. No existing database, volume, or service was changed by these tests.
- A preliminary host run without test connection settings had 60 passed and 19 integration setup failures. No live database was pointed at by that run.
- Only the Schwab Web container was rebuilt and recreated with the existing Compose and HTTPS files. Live anonymous checks at `https://localhost:63656` returned 200 for `/health/ready`, `/`, and `/signed-out`; `/` rendered Sign in and no Sign out, and `/signed-out` rendered the branded heading. The identity provider, private env files, API, database, and Compose definitions were not edited.

## Remaining acceptance

In a signed-in browser, sign out and confirm the provider skips the account picker after `login_hint` appears in the ID token, then confirm `/signed-out`, Sign in in the nav, and that Back does not reveal the dashboard. These provider and browser behaviors were not exercised by the local tests or anonymous live checks.
