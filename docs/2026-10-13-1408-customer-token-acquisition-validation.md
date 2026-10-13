# Customer token acquisition validation — 2026-10-13 14:08 CDT

## Diagnosis

- The `aca3439` diff changed the default authenticate scheme to Cookies and moved the API scope from registration-time OIDC configuration to mutation of a resolved options object after `builder.Build()`.
- A temporary, allowlisted diagnostic on the live Web container captured `InvalidOperationException`, sanitized message **“Cannot determine the cloud Instance”**, and no inner exception during dashboard token acquisition. No token, code, claim value, or credential was logged. The temporary message and inner-type logging was removed before the final build.
- The exception is consistent with Microsoft.Identity.Web selecting the Cookies scheme for token acquisition when no scheme is specified. This is the diagnosed cause; the exact sign-out/sign-in browser actions were not observable from the automation environment, though the live dashboard produced the failure log.

## Change

- Restored the original `Configure<OpenIdConnectOptions>` API scope registration and removed post-build options mutation.
- Kept Cookies as the request authenticate scheme for static navigation. Token acquisition now explicitly requests the `OpenIdConnect` scheme.
- An MSAL interaction-required result, including an empty in-memory token cache after restart, becomes `ManagedReauthenticationRequiredException`; the dashboard's existing guarded flow navigates to `/account/renew` and stops looping after an unsuccessful renewal.
- Preserved the chained customer sign-out handler, `logout_hint`, `/signed-out`, static nav, and authenticated `no-store` responses.

## Verification

- Focused Web tests confirm the configured API scope is present in OIDC options, the code redemption event remains registered, a prior sign-out event handler runs, and the provider requests the configured scope with the OIDC scheme. A provider test maps an interaction-required failure to renewal; dashboard tests cover navigation to `/account/renew` and a failed-renewal fallback.
- `dotnet build SchwabUrlShortener.slnx --no-restore -warnaserror`: 0 warnings, 0 errors.
- Full `dotnet test SchwabUrlShortener.slnx` in disposable containers: 25 unit, 36 Web, 20 integration; **81 passed, 0 failed, 0 skipped**. Disposable PostgreSQL, Redis, Kafka, and test runner containers were removed. Existing databases, volumes, and other services were not changed by the test run.
- Only the Schwab Web container was rebuilt and recreated from the final source. It became healthy. Live anonymous checks at `https://localhost:63656` returned 200 for `/health/ready`, `/`, and `/signed-out`; the nav showed Sign in and the signed-out page rendered.

## Remaining live acceptance

The local tests confirm the code redemption handler is registered, not that a real Entra authorization-code exchange filled the in-memory token cache. A signed-in browser must confirm dashboard managed links load after the Web restart, link creation works, sign-out skips the account picker, and links remain listed after signing in again. This check was requested from the operator without asking for credentials.
