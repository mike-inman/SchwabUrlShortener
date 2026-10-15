## Track H — UI polish, footer, privacy page and Contact Us Handoff

Status: drafted on 2026-10-14

You are working in /Users/michaelinman/Repos/SchwabUrlShortener-ui.

Follow these repo standards exactly:

- `AGENTS.md` is binding. Never print, commit, move or delete `.env*`, credentials, `.claude/`, `.codex/`, or any `docs/010 - *` file.
- Dated documents only: `docs/YYYY-MM-DD-HHMM-<purpose>.md` (America/Chicago time).
- Real implementations and real-service tests only; no mocks standing in for PostgreSQL, Redis or Kafka in integration tests.
- Work only on branch `codex/ui-polish-contact` (already created from `origin/main` at `82ec1b8`). Ask Michael before every push. This change ships through the **gated CI/CD pipeline** (PR → CI → merge → `production` approval → deploy); it is the pipeline's first live end-to-end test, so do not deploy by hand.

Task: Polish the signed-in dashboard, add a site footer, a Privacy and Cookies page, and a Contact Us page with header navigation, and self-host the web fonts.

Goal:

Ziplio looks finished and trustworthy to evaluators and customers, the site needs no cookie consent banner because it sets only strictly necessary cookies and loads nothing from third parties, and visitors can send Ziplio a message that is stored safely and notifies the owner.

Important context:

- Blazor Server (.NET 10) in `src/Shortener.Web`; layout in `Components/Layout/MainLayout.razor` and `NavMenu.razor`; dashboard in `Components/Pages/Dashboard.razor` (line ~52 holds the "Updates automatically" text next to "Your links").
- Strict nonce-based CSP (Track D). `App.razor` currently loads **Google Fonts** from `fonts.googleapis.com` / `fonts.gstatic.com`. That sends every visitor's IP to Google and is the only third-party request on the site; it is also why the CSP allows those origins. Self-hosting removes both.
- Cookies currently set: authentication cookie (OIDC sign-in), antiforgery cookie, and the Container Apps sticky-session affinity cookie for the Web app. All are strictly necessary. Telemetry is server-side OpenTelemetry only; there is no client-side analytics script.
- Rate limiting exists (Track D) with named policies; the guest form uses static server rendering for its POST. Follow the same pattern for the contact form.
- Product brand and visual language: `docs/2026-10-13-ziplio-branding-guidance.md` and the existing site styles. Match them; do not introduce a new component library.
- Owner notification email address: `michael.d.inman@gmail.com`, supplied only through configuration (`Contact__NotifyTo`), never hard-coded or committed in `appsettings.json` for production.

In scope only:

1. **Dashboard:** remove the "Updates automatically" indicator (the dot and text) beside the "Your links" heading. Live updates themselves stay unchanged.
2. **Dashboard:** add consistent horizontal spacing between the "Details" and "Insights" buttons in each link row (use a shared gap on the button group, e.g. `gap: 0.5rem`, not per-button margins). Check narrow widths wrap cleanly.
3. **Footer** on every page: `© 2026 Ziplio` (year computed at render time), links to Privacy & Cookies and Contact, and a muted style consistent with the brand. Must not overlap content on short pages.
4. **Header navigation:** add "Contact" to the header for signed-in and signed-out users, with correct active state and keyboard focus.
5. **Contact Us page** at `/contact`:
   - Fields: name (required, ≤ 100), email (required, valid, ≤ 254), subject (optional, ≤ 150), message (required, 10–4,000 characters).
   - Static server-rendered form POST with antiforgery, matching the guest form pattern; works without JavaScript.
   - Spam controls without third-party CAPTCHA: a named rate-limit policy (5 submissions per 10 minutes per client), a hidden honeypot field, and a minimum fill time (reject submissions under 3 seconds using a server-protected timestamp).
   - On success show a confirmation; on validation failure keep the user's input and show accessible field errors.
   - Persist via a new API endpoint `POST /api/contact` (anonymous, rate-limited) into a new `contact_messages` table (id, created_at_utc, name, email, subject, message, client_key_hash, notification status columns). Add an explicit EF Core migration; it must be additive so older images keep working.
   - Notification: the Analytics worker sends one email per new message (outbox-style: query unsent rows, send, mark sent; bounded retries; failures recorded, never lost). Implement a notifier interface with `ContactNotifications:Mode=None` (default; logs a structured "would notify" entry without message content) and `AzureCommunicationEmail` (Azure Communication Services Email SDK, authenticating with the worker's managed identity; sender and recipient from configuration).
6. **Privacy & Cookies page** at `/privacy`: plain-language description of what Ziplio stores (links, workspace membership, click aggregates, hashed visitor estimates kept 35 days, contact messages), the three strictly necessary cookies and their purpose, no advertising or third-party tracking, and how to contact us. Link it from the footer.
7. **Self-host fonts:** download the currently used font families (open licence) into `wwwroot`, reference them locally, and remove the Google Fonts origins from `App.razor` **and from the CSP**. Verify the CSP still blocks third-party origins and the page renders identically.
8. **No cookie consent banner.** Document in the validation note why one is not required (only strictly necessary cookies, no third-party requests after item 7). If any non-essential cookie or third-party script is found during discovery, stop and report instead.

Out of scope:

- Azure provisioning of Communication Services, its managed sender domain, role assignments and the production `Contact__NotifyTo` / sender settings: Track C does these after this merges. Record exactly what Track C must configure.
- Any change to redirects, analytics, Insights logic, identity, or live-update transport.
- `.github/**` and `infra/**`.
- An admin UI for reading contact messages.

Required discovery before edits:

- `git log -1` must show `82ec1b8` or a later `origin/main`; rebase if `main` has moved.
- Read `Dashboard.razor`, `MainLayout.razor`, `NavMenu.razor`, `App.razor`, the guest form component, and the rate-limiting and CSP setup in the Web and API hosts.
- List every cookie set on `/`, `/dashboard` (signed in, using the test auth handler) and `/contact`, and every third-party origin requested by the pages, before and after your change.
- Find how the guest form implements antiforgery and its rate-limit policy, and reuse that pattern.

Implementation requirements:

- No new client-side JavaScript dependencies; no third-party origins.
- Accessibility: semantic landmarks (`header`, `nav`, `main`, `footer`), labelled inputs, error messages linked with `aria-describedby`, visible focus, colour contrast ≥ 4.5:1.
- Never log message bodies or email addresses; log message id and outcome only. Hash the client key the same way rate-limit logs already do.
- Store contact data only in PostgreSQL; no files, no third-party form services.
- Every new setting has a safe default in `appsettings.json` (Mode=None, no recipient).

Exception / observability requirements:

- `POST /api/contact` returns 201 on success, 400 with field errors on validation failure, 429 with `Retry-After` when rate-limited; honeypot or too-fast submissions return the normal success page but are not stored (log a counted "spam rejected" event).
- Add counters `shortener.contact.received`, `shortener.contact.rejected` (by reason), `shortener.contact.notified`, `shortener.contact.notify_failed`.
- Notification failures retry with backoff up to 5 attempts, then mark the row failed; they never block or fail the visitor's submission.

Realtime / no-polling requirements:

- The contact notifier runs in the Analytics worker's existing background loop pattern with a bounded idle delay (same as the click outbox publisher). No polling is added to any browser page.
- The dashboard live-update behaviour must remain exactly as it is.

Testing:

- Web (bUnit/host): "Updates automatically" absent; button group spacing class present; footer on all pages with current year; header "Contact" link; contact form validation, antiforgery, honeypot, fast-submit rejection, 429.
- Integration (real PostgreSQL): `POST /api/contact` persists a valid message; invalid input rejected; migration applies on a fresh database and on top of current `main`'s schema.
- Worker: with Mode=None, a new message is marked handled without sending; with a test-only in-process sender, success marks sent and a failing sender retries then marks failed.
- CSP and privacy: a test asserts no Google Fonts or other third-party origins appear in the CSP or rendered HTML.
- Run the full disposable Compose suite on a unique project, port and subnet `10.251.50.0/24`; record counts.

Documentation:

- `docs/2026-10-14-HHMM-track-h-ui-polish-contact-validation.md`: before/after screenshots of dashboard, footer, contact and privacy pages (captured locally), cookie and third-party origin inventory, test counts, and the exact Track C configuration list for production email.
- Update `docs/ENGINEERING-SUMMARY.md` only if Michael asks; otherwise list suggested summary changes in the validation note.

Completion criteria:

- [ ] Items 1–8 implemented on `codex/ui-polish-contact`, rebased on current `origin/main`.
- [ ] Zero-warning build, no vulnerable packages, full Compose suite green; counts recorded.
- [ ] No third-party origins in CSP or pages; cookie inventory recorded; no banner required.
- [ ] PR opened to `main` with CI green; Michael approves the merge.
- [ ] After merge, the `production` deployment pauses for Michael's approval, deploys, migrates and passes smoke tests (the first live gated deploy); results linked in the validation note.
- [ ] Track C configuration list for production contact email delivered to Michael.
