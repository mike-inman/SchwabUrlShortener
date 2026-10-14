# Track H UI polish and contact implementation plan

Date: 2026-10-14 13:34 CDT
Purpose: execute `2026-10-14-1325-track-h-ui-polish-contact-handoff.md` in the isolated UI worktree.

## Scope and tasks

1. Remove the dashboard status label and separate row actions; add the header Contact link and a page footer.
2. Add accessible Contact Us and Privacy & Cookies pages. Use static server rendering and antiforgery for the contact form.
3. Add a protected minimum fill time, honeypot, rate limits, anonymous API endpoint, additive PostgreSQL migration, and outbox notification worker.
4. Serve the three existing font families locally and remove Google Fonts from the page and CSP.
5. Validate with focused checks, a fresh disposable Compose stack, a package vulnerability scan, and live HTTP form checks. Record cookie and asset inventories, production configuration, and unfinished gates.

## Acceptance

- Zero-warning solution build, green real-service Compose suite, and no vulnerable packages.
- Real PostgreSQL storage and worker retry behavior verified; invalid and spam requests leave no row.
- No third-party page assets or nonessential cookies found; no consent banner added.
- Only intended files committed on `codex/ui-polish-contact`; no push without Michael's approval.

## Outcome

Implementation and local validation completed on 2026-10-14. See `2026-10-14-1336-track-h-ui-polish-contact-validation.md` for evidence and the gates still open.
