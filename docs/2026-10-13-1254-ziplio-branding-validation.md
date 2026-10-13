# Ziplio branding validation — 2026-10-13 12:54 CDT

Worktree: `/Users/michaelinman/Repos/SchwabUrlShortener-workspace-authorization`, branch `codex/workspace-authorization`.

The API base URL fix was already committed at `b5ba45c`. The unchanged branding kit and handoff were imported alone at `8d0c281`; the Web implementation was committed at `43f8bc6`. This note records the checks after rebuilding **only** the Schwab Web container with Compose. No identity, API client, environment, Compose, database, volume, or other service configuration was changed.

## Local checks

- `dotnet build SchwabUrlShortener.slnx --no-restore`: passed, 0 warnings and 0 errors.
- `dotnet test tests/Shortener.WebTests/Shortener.WebTests.csproj --no-restore`: 27 passed, 0 failed, 0 skipped. This includes the new brand/guest form assertion and existing guest, dashboard, API client, and toast behavior checks.
- `dotnet test tests/Shortener.UnitTests/Shortener.UnitTests.csproj --no-restore`: 25 passed, 0 failed, 0 skipped.
- The full solution test suite was not run. Integration tests target the live local `Schwab_test_local` database and execute `TRUNCATE TABLE click_dead_letters, processed_click_events, click_outbox, url_mappings`; running them here could erase acceptance data. Total executed: **52 passed, 0 failed**.
- `https://localhost:63656/health/ready` returned HTTP 200 after the final Web rebuild. Web, API, analytics, Kafka, PostgreSQL, and Redis reported healthy; only Web was recreated.

## Chromium viewport review

At **1280px**, the settled home screenshot shows the logo, navigation, hero headline, decorative example, proof row, and guest form with the intended dark palette. A valid guest form submission displayed the created-link result. Reusing its custom code with a different destination displayed the error alert. The full-page images are [home](ziplio-validation/2026-10-13-home-1280.png) and [error alert](ziplio-validation/2026-10-13-error-1280.png).

At **390px**, the hero, example, proof row, guest form, created result, and error alert stack within the viewport. Chromium measured `document.documentElement.scrollWidth === clientWidth === 390` on the home and created-result views; the error view also measured 390px. The full-page images are [home](ziplio-validation/2026-10-13-home-390.png), [created result](ziplio-validation/2026-10-13-created-390.png), and [error alert](ziplio-validation/2026-10-13-error-390.png). The created-result capture shows the sticky nav midway down the full-page image because Chromium captured while the page was scrolled; the nav appears at the top when the page is at the top.

With reduced-motion emulation, the hero remained in its final visible state and its animation was `none`. The live-dot style also resolved to `animation: none` using an element matching the dashboard selector. Keyboard traversal showed mint outlines on navigation links and mint focus shadows on form inputs and the primary button at both widths.

## Remaining live acceptance

- The signed-in dashboard and its toast could not be inspected visually in this fresh Chromium context because it has no authenticated customer session. Existing Web component tests exercised dashboard loading, creation, toast text, and automatic updates, but do not prove the live signed-in layout.
- The created guest result displayed a public URL beginning `http://api:8080/r/`. That is a Compose-internal host and is unusable from a normal host browser. This was observed during live creation and needs a separate API/public-URL configuration fix; this branding slice did not change the API client or env/Compose files.
- The External ID hosted-page brand setup remains the separate operator task documented under `branding/entra/portal-setup.md`.
