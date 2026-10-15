## Track H Addendum — Create-form usability fixes Handoff

Status: drafted on 2026-10-14

You are working in /Users/michaelinman/Repos/SchwabUrlShortener-ui.

Follow these repo standards exactly:

- `AGENTS.md` is binding. Never print, commit, move or delete `.env*`, credentials, `.claude/`, `.codex/`, or any `docs/010 - *` file.
- Dated documents only: `docs/YYYY-MM-DD-HHMM-<purpose>.md` (America/Chicago time).
- Work on branch `codex/ui-polish-contact` (PR #9). Ask Michael before every push. Do not merge; Michael merges and approves the production deploy.

Task: Add four usability fixes from Michael's live testing to PR #9, rebased on current `origin/main`.

Goal:

Creating a link is clear and forgiving: users know the custom-code rules before submitting, see exactly which field is wrong, start fresh after a successful create, and see dashboard times in their own time zone.

Important context:

- `origin/main` has moved since PR #9 was opened. It now includes a clearer generic 400 message in `src/Shortener.Web/Services/ShortenerApiClient.cs` ("Check the destination URL (it must start with http:// or https://) and the custom code (4–12 letters or numbers, no spaces or symbols)."). Keep it as the fallback when the API gives no specific reason.
- Custom-code rule (domain, unchanged): 4–12 characters, letters and numbers only, unique across all links (case-insensitive). The API returns 400 with an `ApiError` message for invalid input and 409 for a duplicate code.
- Insights already shows local time (America/Chicago for Michael). The dashboard currently shows UTC (for example "Created 19:31" for a 14:31 local create).
- Applies to both the guest form (home page) and the managed form (dashboard).

In scope only:

1. Rebase `codex/ui-polish-contact` on current `origin/main` and resolve conflicts, keeping both sides' intent.
2. After a successful create, clear the destination URL and custom code fields and return focus to the destination URL field. When creation fails, keep the user's input.
3. Under the custom code field, show the hint "4–12 letters or numbers (optional)". Validate the same rule in the browser before submitting (HTML `pattern` and `maxlength`, plus server validation as the source of truth).
4. Field-specific errors next to the relevant field, linked with `aria-describedby`:
   - invalid URL: "Enter a full URL starting with http:// or https://"
   - invalid code: "Use 4–12 letters or numbers, with no spaces or symbols"
   - duplicate code (409): "That code is already taken. Choose another."
   Use the API's validation reason where it identifies the field; otherwise fall back to the generic message.
5. Show dashboard dates and times in the viewer's local time, consistent with Insights, using the same mechanism Insights uses.

Out of scope:

- Changing the custom-code rules, the database, or the API contract.
- Anything outside the guest form, the managed form, and the dashboard date display.

Required discovery before edits:

- Read how Insights renders local time, and reuse that approach.
- Read how the API reports validation failures (status and `ApiError` text) for the URL and the code, so field mapping is reliable.

Implementation requirements:

- No new client-side dependencies, and no third-party origins (CSP unchanged).
- Accessible: labelled inputs, errors announced, visible focus.

Exception / observability requirements:

- Keep logging as it is; never log submitted URLs or codes beyond what existing logs already record.

Realtime / no-polling requirements:

- No change to live updates; the dashboard keeps its push-based refresh.

Testing:

- Web tests: fields cleared and focus returned after success; input kept after failure; hint visible; each of the three field errors rendered for its case; dashboard renders local time.
- Run the full disposable Compose suite on a unique project, port and subnet `10.251.50.0/24`, and record the counts.

Documentation:

- Append a dated section to Track H's validation note covering these four fixes, with before/after screenshots captured locally.

Completion criteria:

- [ ] Rebased on current `origin/main`; CI green on PR #9.
- [ ] All four fixes implemented with tests; full suite green; counts recorded.
- [ ] Michael asked to merge; production deploy approved by Michael.
