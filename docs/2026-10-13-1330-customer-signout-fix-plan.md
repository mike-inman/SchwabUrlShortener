# Customer sign-out fix plan — 2026-10-13 13:30 CDT

Purpose: ensure the signed-out browser sees request-authenticated navigation, receives uncached authenticated pages, and returns from Entra sign-out to a branded anonymous page.

Scope: Web nav and its small interactive workspace switcher, Web auth response headers and OIDC sign-out event, `/signed-out`, and Web tests. Identity provider settings, private env files, API, database, and Compose definitions are outside this change.

Tasks:

1. Audit fresh anonymous navigation, cache headers, and the current POST sign-out behavior without exposing cookie values.
2. Render the nav per request and move only the workspace selector into an interactive child component.
3. Add `no-store` to authenticated responses, use the `login_hint` claim for OIDC `logout_hint` when present, and land on `/signed-out` after the existing callback.
4. Test nav auth states, the anonymous destination, cookie deletion behavior where an authenticated test session is available, and the preserved workspace switcher.
5. Build, run tests safely against isolated resources, rebuild only Web, and record validation before committing.

Acceptance: after sign-out, a fresh request renders Sign in; local auth cookie chunks are expired; the signed-out page is anonymous and branded; dashboard history cannot be served from browser cache; the workspace selector remains hidden for one workspace and works for multiple workspaces.

Outcome: implemented and validated on 2026-10-13. See `docs/2026-10-13-1349-customer-signout-validation.md` for checks and the remaining live browser acceptance step.
