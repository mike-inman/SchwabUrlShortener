# Track H UI polish and contact validation

Date: 2026-10-14 13:36 CDT
Purpose: record implementation, local checks, privacy inventory, and deployment handoff for Track H.

## Implementation

- Removed the dashboard's “Updates automatically” label and pulse, with no change to live updates. Details and Insights now share a wrapping action group with a `0.5rem` gap.
- Added Contact navigation, a current-year footer, `/privacy`, and a static server-rendered `/contact` form. The form has linked field errors, antiforgery, a hidden honeypot, an API-protected time token, and a client rate limit of five POSTs per ten minutes.
- Added anonymous `GET /api/contact/form-token` and rate-limited `POST /api/contact`; valid messages go to the additive `contact_messages` PostgreSQL table. The Analytics worker handles new rows in `Mode=None` or through Azure Communication Services Email using managed identity, a stable message ID as the email operation ID, up to five attempts, and recorded backoff/failure status. Email status must be confirmed successful before a row is marked notified.
- Copied Open Font License files and variable TTFs for Bricolage Grotesque, Instrument Sans, and JetBrains Mono into Web static assets. Google Fonts loading and its CSP allowances were removed.

## Checks and observations

- `bash scripts/check-codex-handoff.sh docs/2026-10-14-1325-track-h-ui-polish-contact-handoff.md`: passed.
- `dotnet build SchwabUrlShortener.slnx --no-restore`: passed with **zero warnings and errors** after the final code change.
- `dotnet list SchwabUrlShortener.slnx package --vulnerable --include-transitive`: no vulnerable packages in all nine projects, using NuGet's current advisory source.
- Local Track H commit was rebased cleanly first onto Track C's `43777126736c0e999d92244700af78973c451194`, then onto the submission-package main commit `1763c1e47727d9c3c17466f376eef12092bc2b80`. The latter changed only documentation and `.gitignore` upstream; no Track H application file conflicted.
- Fresh `schwab-trackh-check` Compose project on `10.251.50.0/24` and loopback port `18085`, with separate disposable volumes and a generated private test selector: **40/40 integration, 44/44 unit, 69/69 Web** passed. The contact integration test migrated a guarded temporary PostgreSQL schema from the prior main migration, submitted through the real API, and exercised the worker with no-op and test-only success/failure notifiers. The fresh Compose databases also applied the additive migration.
- Live disposable HTTP: `/`, `/contact`, and `/privacy` returned 200; `/dashboard` returned 503 because this guest-only stack has no Entra sign-in. The static form POST worked without JavaScript. An under-three-second submission returned the normal confirmation without storage; a later valid submission created one PostgreSQL row, which the `Mode=None` worker marked handled. Missing antiforgery returned 400. A filled honeypot returned confirmation without storage. The sixth POST returned 429 with `Retry-After`; the row count remained one.
- Local screenshots were captured with isolated headless Chrome profiles at 1440 px width. The before host was a published Web artifact from `1763c1e` on loopback port `18088`; the after host was the rebuilt Track H Compose Web on port `18085`. The before host used a disposable loopback Redis instance and no customer credentials. See the image table below. Signed-in dashboard screenshots remain unavailable because neither local host has Entra sign-in; both return 503 for `/dashboard`. The component tests cover the label removal and action spacing, but signed-in browser live updates and Azure email delivery remain unverified.

## Local before and after screenshots

| View | Before (`1763c1e`) | After (Track H) |
| --- | --- | --- |
| Home navigation and footer | [Home, no Contact navigation or footer](track-h-screenshots/2026-10-14-before-home-footer.png) | [Home with Contact navigation and footer](track-h-screenshots/2026-10-14-after-home-footer.png) |
| Contact | [Route unavailable, 404](track-h-screenshots/2026-10-14-before-contact-404.png) | [Contact form and footer](track-h-screenshots/2026-10-14-after-contact.png) |
| Privacy & Cookies | [Route unavailable, 404](track-h-screenshots/2026-10-14-before-privacy-404.png) | [Privacy page and footer](track-h-screenshots/2026-10-14-after-privacy.png) |
| Signed-in dashboard | Not captured: Entra sign-in unavailable locally | Not captured: Entra sign-in unavailable locally |

Chrome captured the rendered local pages, but this was a guest session. The screenshots do not verify the authenticated dashboard or live update transport.

## Cookies and page origins

Before Track H, source inspection found the authentication cookie, ASP.NET Core antiforgery cookie, and the Container Apps affinity cookie described in the handoff. The only third-party page asset was the Google Fonts stylesheet/font requests from `fonts.googleapis.com` and `fonts.gstatic.com`. No third-party script was found. The live loopback inventory is:

| Version and route | HTTP | `Set-Cookie` names | External HTML asset origins |
| --- | --- | --- | --- |
| Before `/` | 200 | `.AspNetCore.Antiforgery.dgKCqqqflrc` | `https://fonts.googleapis.com`, `https://fonts.gstatic.com` |
| Before `/dashboard` | 503 | None | None (error page) |
| Before `/contact`, `/privacy` | 404 | None | Google Fonts origins on the not-found page |
| After `/` | 200 | `.AspNetCore.Antiforgery.dgKCqqqflrc` | None |
| After `/dashboard` | 503 | None | None (error page) |
| After `/contact`, `/privacy` | 200 | `.AspNetCore.Antiforgery.dgKCqqqflrc` | None |

The inventory parsed `Set-Cookie` headers and rendered HTML `link`, `script`, and `img` URLs from each local route. After Track H, fonts are served from local static assets and the CSP no longer allows Google Fonts. `/dashboard` could not be observed signed in on these guest-only hosts. The test sign-in path verifies an `.AspNetCore.Cookies` authentication cookie; production Container Apps affinity remains an environment cookie to verify after deployment. The strict CSP permits self-hosted fonts. Its `form-action` may still include configured Entra origins for sign-in; these are allowed form destinations, not page asset fetches. No advertising or third-party tracking script is present in the inspected source and local guest pages.

No consent banner was added because the observed/configured cookie purposes are authentication, antiforgery, and requested session affinity. The [ICO's strictly necessary exception](https://ico.org.uk/for-organisations/direct-marketing-and-privacy-and-electronic-communications/guidance-on-the-use-of-storage-and-access-technologies/what-are-the-exceptions/) covers storage essential to provide a requested service; this technical inventory should be revisited if cookies, scripts, or hosting behavior change.

## Track C production configuration

1. Provision an Azure Communication Services resource and linked Email Communication Services domain with a verified MailFrom sender. See the [ACS Email setup guide](https://learn.microsoft.com/en-us/azure/communication-services/quickstarts/email/send-email).
2. Assign the Analytics worker's managed identity send access on the ACS resource. Microsoft's [managed identity email sample](https://learn.microsoft.com/en-us/samples/azure-samples/communication-services-dotnet-quickstarts/email-sample-send-email-with-managed-identity/) specifies Contributor or a custom role with CommunicationServices Read and Write. Verify least privilege against the deployed resource.
3. Set Analytics worker settings `ContactNotifications__Mode=AzureCommunicationEmail`, `ContactNotifications__Endpoint` to the HTTPS ACS endpoint, `ContactNotifications__Sender` to the verified MailFrom address, and `Contact__NotifyTo` to the owner mailbox provided in the handoff. Set `ContactNotifications__ManagedIdentityClientId` only for a user-assigned identity; leave it empty for system-assigned identity. Keep addresses in deployment configuration, not committed appsettings.
4. Confirm the worker can send a real message and that its row becomes notified. `Mode=None` deliberately marks messages handled without sending; messages received before mode activation will not be sent later. Do not claim delivery from local no-op tests.

## Follow-up gates

- Signed-in dashboard before/after screenshots and browser live-update check.
- Michael approved the branch push and PR on 2026-10-14; CI must be green before his separate merge decision. The gated `production` deployment remains a separate approval. No manual deployment is part of Track H.
- Review service-to-service contact traffic capacity: the Web form enforces five per client, while the API also enforces five per source IP. When Web proxies submissions, the API can see a Web instance as one source IP and may throttle several visitors together. A trusted per-visitor partition design is needed before high-traffic production acceptance.
- Suggested engineering-summary update: describe the contact message table, no-op/ACS notification modes, self-hosted fonts, and the local-only evidence; `docs/ENGINEERING-SUMMARY.md` was left unchanged as requested.

## 2026-10-14 19:59 CDT — Create-form usability addendum

The [addendum handoff](2026-10-14-1950-track-h-form-ux-addendum-handoff.md) was copied unchanged into this worktree and passed the handoff checker. `codex/ui-polish-contact` was rebased cleanly onto `origin/main` at `e5fbaad`; the main branch's clearer generic 400 message remains the fallback when an API 400 does not identify a field.

- Guest and managed forms now show the exact custom-code hint, enforce the 4–12 ASCII letter/digit rule with `pattern` and `maxlength`, and show URL, code, and duplicate errors beside the relevant field. Native browser invalid states reveal the same messages beside the inputs; both forms also validate on the server and retain input after rejection. API 400 responses are classified only by known field wording, and raw API reasons are not shown or logged.
- On success, both forms clear the URL and code. Guest static rendering marks the destination input for autofocus on the response page; the interactive dashboard focuses that input after render. The dashboard now imports Insights' `viewerPreferences()` function and converts created and last-accessed timestamps to the browser's time zone, with UTC when the zone is unavailable.
- [Before addendum: guest form](track-h-screenshots/2026-10-14-after-home-footer.png) and [after addendum: guest form](track-h-screenshots/2026-10-14-after-form-ux-home.png) were captured locally at 1440 px width. The after image shows the new hint. A signed-in dashboard before/after screenshot was unavailable in the guest-only local stack; the dashboard behavior was checked through authenticated component tests. These screenshots do not prove live provider sign-in or interactive browser focus.
- `dotnet build SchwabUrlShortener.slnx --no-restore` passed with zero warnings and errors. Focused `ComponentTests` and `ApiClientTests` passed (59/59). A standalone full Web test run passed 84/85; its one environment failure required a real Redis connection, which the isolated Compose run supplies. The first Compose pass had 40/40 integration and 44/44 unit tests; one Web assertion assumed a 12-hour culture and was corrected to compare with `TimeZoneInfo` formatting. Final Compose rerun counts are recorded below.
- Live disposable HTTP on `schwab-trackh-ux-1950` at `http://localhost:18095/`: valid guest POST returned 200 with created-link content, empty inputs, and destination `autofocus`; an invalid URL POST returned 200 with preserved input and the linked URL field error. The rendered home page loaded no third-party asset hosts, and its CSP had no Google Fonts allowance. This used the isolated `10.251.50.0/24` network and its own Compose volumes.

Final Compose rerun: **40/40 integration, 44/44 unit, and 85/85 Web tests passed** in `schwab-trackh-ux-1950` on loopback port `18095` and the isolated `10.251.50.0/24` network. The Web total includes the real Redis test and all new form/time-zone checks. The corrected local-time assertion uses the active culture's format, so it passes in both the host and container test environments.

The PR remains unmerged. The current rebased addendum has not been pushed, so its PR CI result is pending a separately approved push.
