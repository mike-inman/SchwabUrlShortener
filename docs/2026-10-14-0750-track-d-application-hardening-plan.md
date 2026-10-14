# Track D application hardening plan — 2026-10-14 07:50 CDT

## Purpose

Complete items 1–7 of the 2026-10-13 Track D handoff in `/Users/michaelinman/Repos/SchwabUrlShortener-hardening` on `codex/app-hardening`, after item 0 was committed and fast-forwarded to `origin/main` at `514bf64`.

## Scope and tasks

1. Run API, Web, and Analytics containers as the built-in non-root user; retain named Data Protection volumes and provide a safe migration for existing key ownership.
2. Add configurable guest, redirect, and managed rate limits in the proper forwarded-header/authentication order. Cover 429 responses, metrics, and privacy-safe logging.
3. Add Web and API security headers that permit Blazor live connections and Entra flows.
4. Export application telemetry conditionally to Azure Monitor, set sampling, and carry W3C trace context across the outbox and Kafka.
5. Add the configured `www` to apex redirect in Web and API.
6. Add focused tests, run build and suites, and use disposable services for Compose and runtime checks. Update review and README and record evidence in a dated validation document.

## Acceptance criteria

- Every new setting has an `appsettings.json` default in each relevant host.
- `dotnet run` and every Compose variant starts with documented local inputs; no existing project container, volume, secret, or unrelated work is modified during validation.
- Narrow tests and the full build and test suites pass; representative HTTP, non-root, and trace behavior is exercised.
- The final diff contains only Track D files. Stop before the next push to `origin/main` and present the exact commit for approval.

## Outcome

Items 1–7 are implemented in this isolated branch. The dated [validation record](2026-10-14-0825-track-d-application-hardening-validation.md) contains the build, package, HTTP, trace, and disposable Compose evidence. The final source build has zero warnings, and the final fresh Compose suite passed 32 integration, 30 unit, and 64 Web tests. The staged paths and diff check cover only Track D files. Azure Monitor ingestion, provider sign-in, signed-in dashboard rendering, and browser-observed live updates remain deployment acceptance checks for Track C and the operator. The branch will remain local until Michael approves the next push and fast-forward.
