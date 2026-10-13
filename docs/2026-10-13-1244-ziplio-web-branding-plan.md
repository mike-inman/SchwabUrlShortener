# Ziplio Web branding plan — 2026-10-13 12:44 CDT

Purpose: apply the imported provisional Ziplio kit to `Shortener.Web` while preserving the existing authentication, API, and link behavior.

Scope: Web static assets, page markup and titles, a shared brand text class, and Web component assertions. Identity, API client, environment, Compose, databases, and the hosted External ID page remain outside this slice.

Tasks:

1. Copy the three Web assets from the committed branding kit and connect the dark theme, fonts, and favicon.
2. Add the shared brand text and update the navigation, home hero, and page titles.
3. Build and test the changed Web path, then inspect desktop and mobile layouts and accessible states.
4. Commit application changes separately from the imported kit, then record validation in a dated note.

Acceptance: imported kit stays unchanged; Web builds without warnings; existing guest and managed workflows remain covered; the home and dashboard are usable at 1280px and 390px without horizontal page scrolling; reduced motion and keyboard focus remain visible; only the Web container is rebuilt if live inspection is available.

Outcome: the imported kit and Web implementation were committed separately. The Web build and focused tests pass, and the rebuilt Web container is healthy. Live desktop/mobile guest checks and their limits are recorded in `2026-10-13-1254-ziplio-branding-validation.md`.
