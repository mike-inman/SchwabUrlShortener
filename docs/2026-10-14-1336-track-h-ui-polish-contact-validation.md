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
- Browser screenshot capture is **not complete**: the available computer-use inventory reported no browser surface. Dashboard before/after, footer, Contact, and Privacy screenshots need a browser-enabled local session. Signed-in browser live updates and Azure email delivery remain unverified.

## Cookies and page origins

Before Track H, source inspection found the authentication cookie, ASP.NET Core antiforgery cookie, and the Container Apps affinity cookie described in the handoff. The only third-party page asset was the Google Fonts stylesheet/font requests from `fonts.googleapis.com` and `fonts.gstatic.com`. No third-party script was found.

After Track H, live guest GETs on `/`, `/contact`, and `/privacy` set only `.AspNetCore.Antiforgery.*`; the parsed rendered page assets had no external origins. `/dashboard` could not be observed signed in on this guest-only stack. The test sign-in path verifies an `.AspNetCore.Cookies` authentication cookie; production Container Apps affinity remains an environment cookie to verify after deployment. The strict CSP permits self-hosted fonts. Its `form-action` may still include configured Entra origins for sign-in; these are allowed form destinations, not page asset fetches. No advertising or third-party tracking script is present in the inspected source and local guest pages.

No consent banner was added because the observed/configured cookie purposes are authentication, antiforgery, and requested session affinity. The [ICO's strictly necessary exception](https://ico.org.uk/for-organisations/direct-marketing-and-privacy-and-electronic-communications/guidance-on-the-use-of-storage-and-access-technologies/what-are-the-exceptions/) covers storage essential to provide a requested service; this technical inventory should be revisited if cookies, scripts, or hosting behavior change.

## Track C production configuration

1. Provision an Azure Communication Services resource and linked Email Communication Services domain with a verified MailFrom sender. See the [ACS Email setup guide](https://learn.microsoft.com/en-us/azure/communication-services/quickstarts/email/send-email).
2. Assign the Analytics worker's managed identity send access on the ACS resource. Microsoft's [managed identity email sample](https://learn.microsoft.com/en-us/samples/azure-samples/communication-services-dotnet-quickstarts/email-sample-send-email-with-managed-identity/) specifies Contributor or a custom role with CommunicationServices Read and Write. Verify least privilege against the deployed resource.
3. Set Analytics worker settings `ContactNotifications__Mode=AzureCommunicationEmail`, `ContactNotifications__Endpoint` to the HTTPS ACS endpoint, `ContactNotifications__Sender` to the verified MailFrom address, and `Contact__NotifyTo` to the owner mailbox provided in the handoff. Set `ContactNotifications__ManagedIdentityClientId` only for a user-assigned identity; leave it empty for system-assigned identity. Keep addresses in deployment configuration, not committed appsettings.
4. Confirm the worker can send a real message and that its row becomes notified. `Mode=None` deliberately marks messages handled without sending; messages received before mode activation will not be sent later. Do not claim delivery from local no-op tests.

## Follow-up gates

- Visual screenshots and signed-in dashboard/browser live-update check.
- PR and CI after Michael approves a push; merge and `production` approval are separate gates. No manual deployment is part of Track H.
- Review service-to-service contact traffic capacity: the Web form enforces five per client, while the API also enforces five per source IP. When Web proxies submissions, the API can see a Web instance as one source IP and may throttle several visitors together. A trusted per-visitor partition design is needed before high-traffic production acceptance.
- Suggested engineering-summary update: describe the contact message table, no-op/ACS notification modes, self-hosted fonts, and the local-only evidence; `docs/ENGINEERING-SUMMARY.md` was left unchanged as requested.
