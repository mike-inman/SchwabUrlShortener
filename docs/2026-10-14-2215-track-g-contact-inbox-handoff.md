## Track G — Admin contact inbox Handoff

Status: drafted on 2026-10-14

You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:

- Read `AGENTS.md`; confirm `pwd -P`, branch, commit, and `git status --short --branch`. Treat existing untracked files as user-owned.
- Work in a new worktree from `origin/main`: `git fetch origin && git worktree add ../SchwabUrlShortener-contact-inbox -b codex/contact-inbox origin/main`.
- Preserve `.env*`, credentials, `.claude/`, `.codex/`, and unrelated changes. Never weaken authentication or authorization to make a test pass.
- Open one PR; Michael merges and approves the deploy. Production configuration goes through Bicep, with what-if approval before any deploy.

Task: Give Michael a signed-in, admin-only page on ziplio.io where he can read contact-form messages, independent of email delivery.

Goal:

Michael signs in, sees a "Messages" link in the header (visible only to admins), and at `/admin/messages` reads every contact submission: newest first, with name, email, subject, message, received time in his local time zone, and notification status (sent / pending / failed). He can mark messages handled. Non-admins get 403 from the API and never see the link or the page content.

Important context:

- Submissions are stored by the API (`POST /api/contact`, `src/Shortener.Api/ContactEndpoints.cs`) in `contact_messages` (`src/Shortener.Infrastructure/ContactMessage.cs`: Id, CreatedAtUtc, Name, Email, Subject, Message, ClientKeyHash, NotifiedAtUtc, FailedAtUtc, NextAttemptAtUtc, NotificationAttempts, LastNotificationErrorCode). Today there is no way to read them except SQL against a private PostgreSQL server.
- There is no admin role yet. Identity is Entra External ID (customer tenant `6adc36e9-dfe5-469f-8aed-6ef83abcdd19`); the API validates delegated tokens with scope `access_as_user` and maps users to `application_users` and `external_identities` (issuer + subject).
- Email notification is being added separately (`2026-10-14-2215-track-c-contact-email-acs-handoff.md`); this inbox is the guaranteed fallback and must not depend on it.
- UI conventions: Blazor Server, `@rendermode InteractiveServer` for authenticated pages, live updates through the existing Redis hint plus WebSocket path (no polling, no Refresh button), dark Ziplio styling, times via the existing viewer time-zone helper used on the dashboard.

In scope only:

1. Admin authorization in the API: a configuration list `Admin:Subjects` holding issuer + subject pairs (or Entra object IDs, whichever the API already uses as the stable key; document the choice), and a named policy `ContactInboxAdmin` applied to the new endpoints. No admin when the list is empty.
2. API endpoints: `GET /api/admin/contact-messages?cursor=…&take=50` (newest first, keyset pagination) and `POST /api/admin/contact-messages/{id}/handled` (sets a new nullable `HandledAtUtc`, idempotent). `GET /api/me` (or the existing caller-info endpoint) returns `isContactAdmin`.
3. An EF Core migration adding `handled_at_utc` to `contact_messages`; it must be backward compatible (nullable column, no data rewrite) because the deploy pipeline migrates before rolling out.
4. Web: `/admin/messages` page (`[Authorize]`, InteractiveServer) and a header "Messages" link with an unhandled count, rendered only when `isContactAdmin` is true; a live update when a new message arrives (publish an existing-style hint on the admin scope from the API after insert).
5. Bicep: add `Admin__Subjects__0` (Michael's identity key) to `ca-shortener-api` env, behind what-if approval. Look up Michael's value from his `application_users` / `external_identities` row or from the Entra customer tenant via `az`, and confirm it with Michael before deploying.

Out of scope:

- Replying to messages from the app, deleting messages, exports, or bulk actions.
- Email sending (Track C), changes to the public contact form, spam rules, or rate limits.
- A general role-management UI.

Required discovery before edits:

- Read `ContactEndpoints.cs`, `ContactMessage.cs`, the DbContext mapping, the most recent migrations, API authentication setup in `src/Shortener.Api/Program.cs`, and how the Web obtains the caller's workspaces (`/api/me/workspaces`) to follow the same token flow.
- Check how live-update hints are published (`shortener:links:v1`, scope and scopeId) and subscribed in Web, so you can add an `admin` scope without breaking existing subscribers.

Implementation requirements:

- Authorization is enforced in the API; the Web hiding the link is only cosmetic. Every admin endpoint returns 401 when unauthenticated and 403 for an authenticated non-admin; add tests for both.
- Render message fields as text only (Blazor encoding; no `MarkupString`). Show the submitter email as a `mailto:` link.
- Do not expose `ClientKeyHash` in any API response.
- Pagination must be stable under concurrent inserts (keyset on `(created_at_utc, id)`).
- Keep the endpoints out of the public guest rate limiter but under the authenticated limits already used for managed APIs.

Exception / observability requirements:

- Structured logs for admin reads and handled-marks with user id and message id only (no message content or submitter email). Use the existing correlation and trace propagation. Return safe ProblemDetails on errors.

Realtime / no-polling requirements:

- New messages appear on an open `/admin/messages` page, and the header count updates, through the existing Redis hint plus Blazor WebSocket path, debounced like the dashboard. No timers polling the API and no Refresh button.

Testing:

- API integration tests: 401, 403, 200 for the admin; pagination ordering; handled is idempotent; `ClientKeyHash` is absent from JSON.
- Migration applies on a copy of the current schema and is reversible.
- bUnit: link hidden for non-admins and shown with a count for admins; page renders text safely (a message containing `<script>` renders literally).
- `dotnet build` with zero warnings; full `dotnet test` in an isolated Compose project.

Documentation:

- Runbook section "Reading contact messages" (how to sign in, who is admin, how to add another admin through Bicep).
- Append a dated "Result" section here with PR, test counts, and the production check (Michael sees his test message at `/admin/messages`).

Completion criteria:

- [ ] Admin-only API endpoints with 401 and 403 tests; `ClientKeyHash` never returned.
- [ ] `/admin/messages` shows messages newest first in local time with notification status, mark-handled, and a live update.
- [ ] Header link and count visible only to admins.
- [ ] Michael's identity configured as admin through approved Bicep; he confirms he can read a real submission in production.
- [ ] Runbook and Result section updated.

## Result — 2026-10-15

- Implemented the admin policy, keyset inbox API, idempotent handled action, nullable reversible migration, signed-in Web inbox, admin-only header count, and Redis-hint live updates. The API returns 401 for anonymous callers and 403 for authenticated non-admins in integration tests. The response omits `ClientKeyHash`.
- Local `dotnet build SchwabUrlShortener.slnx --no-restore -v quiet` passed with zero warnings. A full isolated Compose run passed 42 integration, 44 unit, and 88 Web tests. The migration was applied, reversed, and reapplied against a disposable PostgreSQL schema. Bicep compilation passed; four existing linter warnings remain outside this slice.
- The operator runbook is `docs/runbooks/2026-10-14-2233-contact-inbox.md`. PR: [#19](https://github.com/mike-inman/SchwabUrlShortener/pull/19). The full isolated suite passed again after merging current `origin/main` (42 integration, 44 unit, 88 Web). The configured Azure CLI account is in a different tenant and lists no subscription in the customer tenant, so Michael's identity key could not be confirmed through that path. The exact key, scoped what-if approval, deployment, and signed-in production check remain open. A real contact submission has not yet been verified at `/admin/messages`.
