# Internal Compose API address fix plan — 2026-10-13 12:16 CDT

Purpose: fix the signed-in dashboard's rejected `http://api:8080` base address during customer live acceptance without broadening trust in arbitrary HTTP endpoints.

Scope: a shared Web API address validator, Web readiness, focused tests, example configuration, and the two private Schwab Web env files. Preserve all credentials, other services, databases, and volumes. Restart only the Schwab Web container.

Tasks:
1. Keep HTTPS and loopback HTTP accepted by default; allow explicitly listed internal DNS hosts on HTTP with exact host matching.
2. Use that validator in the Web API client and readiness endpoint; log a sanitized startup warning for rejected configuration.
3. Test rejected default Compose URL, explicit `api` allowance, unlisted remote HTTP rejection, and unaffected HTTPS.
4. Back up the private Web env files before adding the allowlist, then rebuild and restart only Web.
5. Verify solution build/tests, Compose health, and live dashboard behavior available without a user token.

Acceptance: dashboard can address the internal API when configured; rejected addresses fail readiness; all intended changes are committed; user authentication and data remain intact. Browser managed-link acceptance remains the operator's next step.

Outcome: implemented. Focused Web tests, full Web and unit suites, solution build, Compose config, and Web readiness passed. The Web container was rebuilt and restarted alone. Browser managed-link acceptance is pending the operator's refreshed session; see the dated validation note.
