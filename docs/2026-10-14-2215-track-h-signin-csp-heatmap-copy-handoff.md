## Track H — Sign-in CSP root cause, Insights heatmap, and one-click copy Handoff

Status: drafted on 2026-10-14

You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:

- Read `AGENTS.md`; confirm `pwd -P`, branch, commit, and `git status --short --branch` before edits. The main checkout has many user-owned untracked files under `docs/`, `branding/`, and `CLAUDE.md`; do not touch, stage, stash, or delete them.
- Do all work in a new worktree created from current `origin/main`, not in the main checkout: `git fetch origin && git worktree add ../SchwabUrlShortener-signin-csp -b codex/signin-csp-heatmap-copy origin/main`.
- Preserve `.env*`, credentials, `.claude/`, `.codex/`, and unrelated changes. Do not weaken any security control beyond the single, named CSP source addition below.
- Push the branch and open one PR. Do not merge it; Michael merges and approves the production deploy.

Task: Fix the real cause of "Sign in does nothing", make the "When your audience clicks" heatmap show click intensity, and add a one-click copy button for short links.

Goal:

1. Clicking any link to `/account/signin` (or `/account/renew`) from any page reaches the Entra sign-in page every time, because Blazor's built-in enhanced-navigation fallback is no longer blocked by our own CSP.
2. The Insights heatmap shows one sequential color scale where cell brightness reflects click count, rows ordered Monday→Sunday top to bottom, with a tooltip giving day, hour, and clicks.
3. Every short link shown to a user (dashboard "Your links" table and the newly created guest/managed link result) has a copy button that copies the full absolute short URL with one click and confirms "Copied".

Important context:

- Root cause was reproduced live on ziplio.io on 2026-10-14 (Claude session `https://claude.ai/code/session_01R4UGezFHnqDx2vpBB3GPCq`). From `/contact`, clicking "Sign in" changed the address bar to `/account/signin` but the page stayed on Contact. Console: `Connecting to 'https://schwaburlshortener.ciamlogin.com/.../oauth2/v2.0/authorize?...' violates the following Content Security Policy directive: "connect-src 'self' wss:"` followed by `Uncaught (in promise) TypeError: Failed to fetch` from `blazor.web.js`.
- Blazor's enhanced navigation fetches the target with `mode: "no-cors"`. When an endpoint redirects to another origin, the fetch normally yields an `opaque` response and `blazor.web.js` deliberately falls back to a full page load (`Enhanced navigation failed for destination ... Falling back to full page load`). Our CSP in `src/Shared/HardeningHostConfiguration.cs` (`UseSecurityHeaders`, web branch) sets `connect-src 'self' wss:`, which blocks the redirect hop, so the fetch throws and the fallback never runs.
- `UseSecurityHeaders` already computes `formOrigins` (the HTTPS origins of `AzureAd:Authority` / `AzureAd:Instance`, i.e. `https://schwaburlshortener.ciamlogin.com` in production) and appends them to `form-action`. The fix appends the same set to `connect-src`. No other CSP directive changes.
- PR #14 (`fix/signin-full-page-navigation`, adds `data-enhance-nav="false"` to the four sign-in anchors) is complementary defense in depth and may merge before or after this PR. If #14 has merged first, rebase on it. Do not duplicate or revert its changes.
- Sign-out is a plain `<form method="post" action="/account/signout">` without `data-enhance`; `blazor.web.js` only enhances forms that opt in, so sign-out is a native navigation and is not affected by this bug. Michael's "sign out does nothing" screenshot showed `/account/signin` in the address bar on the Signed-out page, i.e. the "Sign in again" link hitting the same sign-in bug.
- Heatmap bug: `src/Shortener.Web/Components/Pages/Insights.razor.cs` `ConfigureCharts()` sets `heatOptions.PlotOptions.Heatmap.EnableShades = false` with no `ColorScale` and no `Colors`, so ApexCharts gives each weekday series its own palette color and ignores values. Every cell in a row renders the same color.
- Existing JS module pattern: `src/Shortener.Web/wwwroot/insights.js` imported via `IJSRuntime`. CSP `script-src` is `'self' 'nonce-…'`, so same-origin script files are allowed; inline handlers are not.
- The guest link result on `Home.razor` is static SSR (no interactive circuit); the dashboard is `@rendermode InteractiveServer`. The copy mechanism must work on both.

In scope only:

1. `src/Shared/HardeningHostConfiguration.cs`: add the identity-provider origins (`formOrigins`) to `connect-src`, and update `tests/Shortener.WebTests/HardeningHttpTests.cs` to assert both the existing `connect-src 'self' wss:` prefix and the configured authority origin when Entra is configured, and no extra origin when it is not.
2. `src/Shortener.Web/Components/Pages/Insights.razor.cs` and `Insights.razor`: heatmap color scale, row order, tooltip, and an accessible text summary that still works with zero clicks.
3. A copy-to-clipboard button for short links in `Dashboard.razor` (links table and creation result) and `Home.razor` (guest link result), implemented with one new same-origin script file (for example `wwwroot/copy-link.js`) using document-level event delegation on `button[data-copy-text]`, loaded from `App.razor` with `type="module"`.
4. Tests for each change (see Testing).

Out of scope:

- Any change to OIDC configuration, cookie settings, the sign-out endpoint, Entra app registrations, or `form-action`.
- Disabling enhanced navigation globally or removing the CSP.
- Other charts on Insights, dashboard layout changes beyond the copy button, contact form or email work (separate handoffs).
- Bicep or production configuration changes.

Required discovery before edits:

- `git log --oneline -3 origin/main`; check whether PR #14 has merged (`gh pr view 14 --json state`).
- Read `src/Shared/HardeningHostConfiguration.cs` `UseSecurityHeaders` and every test that asserts on `Content-Security-Policy` (`grep -rn "Content-Security-Policy" tests`).
- Read `Insights.razor`, `Insights.razor.cs` (`BuildHeat`, `ConfigureCharts`, `HeatSummary`), and the Blazor-ApexCharts version in `Shortener.Web.csproj` so the `ColorScale` / `Ranges` API used actually exists in that version.
- Find where short links render: `grep -n "ShortUrl\|shortUrl" src/Shortener.Web/Components/Pages/*.razor`. Confirm how the absolute URL is composed (public base URL vs relative `/r/{code}`); the copied text must be absolute (`https://ziplio.io/r/{code}` in production).

Implementation requirements:

- CSP: `connect-src 'self' wss: <idp-origins>` where `<idp-origins>` is exactly the existing `formOrigins` set (HTTPS scheme + authority only, no path). When Entra is not configured the directive must remain `connect-src 'self' wss:`. Keep the code path single-sourced (one set, used in both directives).
- Heatmap:
  - one hue (`#59E6CF`, the brand teal) with a sequential scale; zero-click cells use a low-contrast neutral that is visibly "empty" against the dark card (e.g. `#1E2C44`), not the brand color;
  - ranges derived from the actual max count (e.g. 0, then quartiles of 1..max) so a link with 3 clicks still shows contrast; when max is 0 render all cells neutral and show "No clicks in the last 28 days";
  - rows Monday at top through Sunday at bottom (ApexCharts draws the first series at the bottom, so add series in reverse order);
  - tooltip text `Tue 3p · 4 clicks` (singular "click" for 1); hide per-cell data labels;
  - keep the visually hidden summary and make it state the busiest day/hour and total.
- Copy button:
  - a `<button type="button" class="copy-link" data-copy-text="{absolute url}" aria-label="Copy short link {code}">` with an icon (inline SVG, `aria-hidden="true"`) placed next to each short link;
  - `copy-link.js` registers one `click` listener on `document` (idempotent if loaded twice), uses `navigator.clipboard.writeText`, and falls back to selecting a hidden textarea + `document.execCommand('copy')` only if the Clipboard API is unavailable;
  - on success swap the icon to a check and set visible text "Copied" for 2 seconds, and announce "Copied" via an `aria-live="polite"` region; on failure show "Copy failed" (no exception escapes);
  - works after Blazor enhanced navigation and after interactive re-renders because of document-level delegation; no inline `onclick` attributes (CSP).
- Do not log the copied URL or any user data to the console.

Exception / observability requirements:

- CSP change: no new runtime paths. Copy button: client-side only; failures surface as the "Copy failed" state, no server logging. Heatmap: if insights data is unavailable keep the existing error/empty handling.

Realtime / no-polling requirements:

- The heatmap continues to update through the existing live-update hint path on the Insights page. Do not add polling or a Refresh button (AGENTS.md "Live operator UI").

Testing:

- `dotnet build SchwabUrlShortener.slnx` with zero warnings.
- Web tests: CSP header contains the authority origin in `connect-src` when Entra is configured and exactly `connect-src 'self' wss:` when not; `form-action` unchanged.
- bUnit: Insights heatmap options contain a color scale and series are ordered Monday→Sunday top-down; zero-click data renders the empty-state summary.
- bUnit: dashboard rows and the guest/managed result render a `button.copy-link` whose `data-copy-text` is the absolute short URL.
- Full `dotnet test SchwabUrlShortener.slnx` in an isolated Compose project as in previous Track H runs; record counts.
- Manual, local: from `/contact`, click Sign in with a dev authority configured and confirm the browser leaves the page (or document why local Entra is unavailable); click a copy button and paste.

Documentation:

- Append a dated "Result" section to this handoff with commit, PR number, check results, and anything not verified (for example live Entra sign-in).
- Add one line to `docs/ENGINEERING-SUMMARY.md` oversight log: "CSP `connect-src` blocked Blazor's enhanced-navigation fallback for the external sign-in redirect; fixed by allowing the IdP origin."

Completion criteria:

- [ ] PR opened from `codex/signin-csp-heatmap-copy` with only the in-scope files; CI green (all required checks).
- [ ] `connect-src` includes the configured IdP origin; tests prove both configured and unconfigured cases.
- [ ] Heatmap uses one sequential scale, Monday→Sunday, tooltip and empty state as specified; tests pass.
- [ ] Copy button present on dashboard rows, creation result, and guest result; copies the absolute URL; "Copied" feedback and live-region announcement.
- [ ] Result section recorded in this handoff.

## Result — 2026-10-14 22:40 CDT

Implementation: the Web CSP adds the existing HTTPS IdP-origin set to `connect-src` while preserving `form-action`. The heatmap has a value-based teal scale with a neutral zero range, reverse series order for Monday at the top, a day/hour/count tooltip, hidden data labels, and a zero-click summary. Guest results, dashboard rows, the managed creation notice, and selected-link details show a button for the full public `/r/<code>` URL. A same-origin module delegates clicks and provides clipboard fallback and accessible success/failure feedback.

Verification so far: the handoff checker passed. The solution build passed with zero warnings. Focused Web tests passed 43/43 after the final heatmap layout change. An isolated Compose run passed 40/40 integration, 44/44 unit, and 86/86 Web tests on a disposable database and Kafka/Redis stack; it completed before the final zero-click rendering adjustment, so the full Compose gate will be repeated after rebasing on PR #14. A local headless Chrome check exercised delegated copy, duplicate module loading, visible/live feedback, and the fallback branch; both checks passed. `node --check` passed.

PR #14 merged to `main` during this work. This branch will be rebased on that merge before its own PR. Local Entra sign-in navigation and a system-clipboard paste have not been verified; the local stack has no dev authority or customer sign-in credentials. No production deployment or live Entra acceptance is claimed.

## Final result — 2026-10-15 01:49 CDT

PR [#18](https://github.com/mike-inman/SchwabUrlShortener/pull/18) is open from `codex/signin-csp-heatmap-copy`, based on `main` after PR #14. Implementation commits are `8c1629e` and `47fc8cd`. The latter replaces the inline Apex tooltip function with the supported `ApexPointTooltip` component and confirms singular and plural click text. Only the in-scope application files, tests, and the two requested documents are in the PR.

Final local checks: `dotnet build SchwabUrlShortener.slnx --nologo` passed with zero warnings and errors; focused Web component and CSP header tests passed 43/43; `node --check`, the handoff checker, and `git diff --check` passed. The final source passed the full suite in a fresh isolated Compose project: **40/40 integration, 44/44 unit, and 86/86 Web tests**. Its PostgreSQL, Redis, and Kafka project, network, and volumes were removed after the run. A standalone Web run passed 85/86; its sole failure was the real-Redis test with no host Redis connection, which passed in Compose.

The earlier local headless Chrome check covered delegated copy, duplicate module loading, visible and live feedback, and the Clipboard API fallback. A system clipboard paste, authenticated dashboard browser session, and local Entra sign-in navigation remain unverified because this local environment has no development authority or signed-in account. CI is running for PR #18; production deployment and live provider acceptance remain separate gates.

## CodeQL follow-up — 2026-10-15 01:59 CDT

The first PR check reported an integer-overflow risk in the heatmap quartile calculation. The calculation now uses division and remainder, and a new regression test checks contiguous color ranges through `long.MaxValue`. The corrected source builds with zero warnings and errors, focused Web tests pass 44/44, and a second fresh isolated Compose run passes **40/40 integration, 44/44 unit, and 87/87 Web tests**. The second disposable Compose project, network, and volumes were removed. The updated PR commit will receive a new CodeQL scan; the earlier scan failure must not be treated as resolved until that check passes.
