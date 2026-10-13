# Customer token acquisition regression plan — 2026-10-13 14:01 CDT

Purpose: restore managed API token acquisition after `aca3439` while preserving the working customer sign-out flow.

Scope: Web OIDC and token acquisition registration, token cache renewal behavior, focused Web regression tests, and a dated validation record. Existing identity provider settings, private env files, API, database, and Compose definitions stay unchanged.

Tasks:

1. Compare `aca3439` with `5466bb0`, temporarily record a sanitized token acquisition failure message and inner exception type, and reproduce where an authenticated browser session is available.
2. Restore the original configured OIDC scope path, use the explicit OIDC scheme for token acquisition if the cookie default causes incorrect option selection, and verify existing Microsoft.Identity.Web event handlers remain chained.
3. Route an empty token cache through `/account/renew` once, with a clear fallback after a failed renewal.
4. Add focused regression coverage for scope, event chaining, token acquisition scheme, and renewal; run the full suite with disposable services.
5. Remove diagnostic logging, rebuild only Web, check live readiness, and record remaining browser acceptance.

Acceptance: fresh customer sign-in can acquire the configured API scope; dashboard managed links load; empty cache triggers renewal; sign-out retains `logout_hint`, static nav, no-store, and `/signed-out`; build and tests pass without touching live data.

Outcome: implemented and locally validated on 2026-10-13. See `docs/2026-10-13-1408-customer-token-acquisition-validation.md`. Real Entra code redemption and browser acceptance remain for the signed-in operator.
