## Track C — Contact form email via Azure Communication Services on ziplio.io Handoff

Status: drafted on 2026-10-14

You are working in /Users/michaelinman/Repos/SchwabUrlShortener.

Follow these repo standards exactly:

- Read `AGENTS.md`; confirm `pwd -P`, branch, commit, and `git status --short --branch`. Treat existing untracked files as user-owned.
- Work in a new worktree from `origin/main`: `git fetch origin && git worktree add ../SchwabUrlShortener-contact-email -b codex/contact-email-acs origin/main`.
- All Azure work: account `social@mediaraq.com`, subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, resource group `rg-schwab-url-shortener` only. Infrastructure as code (Bicep) and Azure CLI only, never the portal.
- Show `az deployment group what-if` output to Michael and wait for his explicit approval before any `az deployment group create`. Do not remove or modify the `CanNotDelete` resource lock.
- DNS stays at GoDaddy. Michael adds DNS records himself; you produce the exact records. Never edit, replace, or delete an existing DNS record on his behalf.
- No secrets in code, logs, docs, or chat. The notifier authenticates with the Analytics managed identity.

Task: Provision Azure Communication Services Email with `ziplio.io` as a verified custom domain and turn on contact-form email notifications to `mike.inman@ziplio.io`.

Goal:

Every new contact-form submission on ziplio.io sends one email from `Ziplio <noreply@ziplio.io>` to `mike.inman@ziplio.io`, with `Reply-To` set to the submitter's address so Michael can reply directly. The email passes DMARC for ziplio.io (which is `p=quarantine`) and lands in the inbox.

Important context:

- Code already exists: `src/Shortener.Analytics/ContactNotificationDispatcher.cs` has `AzureCommunicationContactNotifier` (Azure.Communication.Email, `ManagedIdentityCredential`, idempotent `SendAsync` keyed by message id) and a durable dispatcher with retries and `NotifiedAtUtc` / `FailedAtUtc` / `LastNotificationErrorCode`. Mode is selected by `ContactNotifications:Mode` (`None` | `AzureCommunicationEmail`) and needs `ContactNotifications:Endpoint`, `ContactNotifications:Sender`, `ContactNotifications:ManagedIdentityClientId`, `Contact:NotifyTo`. Production currently runs `None`, so submissions are stored in `contact_messages` and only logged.
- With `None`, the dispatcher treats messages as handled, so messages submitted so far have `NotifiedAtUtc` set and will not be emailed when the mode changes.
- Current ziplio.io mail DNS (resolved 2026-10-14): MX `0 ziplio-io.mail.protection.outlook.com` (Microsoft 365 provisioned via GoDaddy, tenant `NETORGFT6001121.onmicrosoft.com`); SPF TXT `v=spf1 include:secureserver.net -all`; DMARC `v=DMARC1; p=quarantine; adkim=r; aspf=r; rua=mailto:dmarc_rua@onsecureserver.net;`.
- Pricing (Microsoft Learn email pricing page): about $0.00025 per email plus $0.00012 per MB, so contact-form volume costs cents per month. Custom-domain limits are 30 emails/minute and 100/hour by default; Azure-managed domains are only 5/minute and 10/hour and send from an `azurecomm.net` address, so the custom domain is required.
- Analytics container app `ca-shortener-analytics` uses a user-assigned identity (`analyticsId` in `infra/p2-apps.bicep`); its env list is built in that file.

In scope only:

1. New Bicep (for example `infra/p2-contact-email.bicep` or a module referenced by `p2-apps.bicep`) for: `Microsoft.Communication/emailServices` (data location United States), its `domains` child `ziplio.io` (`domainManagement: CustomerManaged`, user engagement tracking disabled), a `senderUsernames` child `noreply` with display name `Ziplio`, and `Microsoft.Communication/communicationServices` with `linkedDomains` set to the verified domain. Outputs: the verification records (`verificationRecords` for Domain, SPF, DKIM, DKIM2, DMARC) and the ACS endpoint.
2. A role assignment granting the Analytics managed identity permission to send email on the Communication Service (use the least-privileged built-in role that supports Entra-authenticated email send; confirm the role name and ID with `az role definition list` and document the choice).
3. `ca-shortener-analytics` env settings: `ContactNotifications__Mode=AzureCommunicationEmail`, `ContactNotifications__Endpoint=<acs endpoint>`, `ContactNotifications__Sender=noreply@ziplio.io`, `ContactNotifications__ManagedIdentityClientId=<analyticsId clientId>`, `Contact__NotifyTo=mike.inman@ziplio.io`. Gate them behind a Bicep parameter (default off) so they deploy only after the domain is verified.
4. `AzureCommunicationContactNotifier`: add `ReplyTo` = the submitter's validated email (with display name = submitted name), keep the subject `Ziplio contact: {subject or "(no subject)"}` (truncate to 120 characters, strip CR/LF), and keep plain text. Unit tests for the message construction, including CR/LF and long subjects.
5. A one-time, documented, opt-in SQL command (in the runbook, not run automatically) that re-queues messages received before email was enabled (`UPDATE contact_messages SET notified_at_utc = NULL, next_attempt_at_utc = now() WHERE notified_at_utc IS NOT NULL AND created_at_utc < '<cutover>'`), executed only if Michael asks.

Out of scope:

- Editing GoDaddy DNS, Microsoft 365, or Exchange settings yourself.
- Any SMTP credentials, Gmail/Outlook app passwords, or third-party email providers.
- An admin inbox UI (separate handoff `2026-10-14-2215-track-g-contact-inbox-handoff.md`).
- Changes to the Web or API contact endpoints, rate limits, or spam checks.

Required discovery before edits:

- `az account show` is `social@mediaraq.com` on subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`; `az group show -n rg-schwab-url-shortener`.
- `az provider show -n Microsoft.Communication --query registrationState`; register it if needed (that is a subscription-level change: ask Michael first).
- Read `infra/p2-apps.bicep` (analytics env, identities), `infra/README.md`, and `ContactNotificationDispatcher.cs` and its tests.
- Confirm the current SPF record has exactly one `v=spf1` TXT at the apex before proposing the merged record.

Implementation requirements:

- Phase 1 (approval required): what-if, then deploy the email service, domain, sender username, and communication service with `linkedDomains` empty or omitted until verification succeeds.
- Phase 2 (Michael at GoDaddy): give him a table of the exact records from the domain's `verificationRecords`:
  - `TXT @ ms-domain-verification=…`, added as a second TXT record, not replacing anything;
  - the SPF change as a single merged record. Expected result: `v=spf1 include:secureserver.net include:spf.protection.outlook.com -all`, but verify against what ACS returns. One SPF record only: edit the existing one, do not add a second;
  - two DKIM CNAMEs: `selector1-azurecomm-prod-net._domainkey` and `selector2-azurecomm-prod-net._domainkey`, with the exact targets from the output;
  - leave DMARC unchanged.
  Then start verification with `az communication email domain initiate-verification` for each type and poll `az communication email domain show` until Domain, SPF, DKIM and DKIM2 are `Verified`. DNS can take up to an hour.
- Phase 3 (approval required): link the domain to the communication service, add the role assignment and the env settings (parameter on), what-if, deploy, and confirm a new Analytics revision is healthy.
- Never put the ACS connection string or access keys anywhere; Entra and managed identity only. Disable local auth on the communication service if the API version supports `disableLocalAuth`.

Exception / observability requirements:

- Keep the dispatcher's existing retry, backoff, and dead-state behavior. Log the ACS operation id and message id on success and the error code on failure, never the message body, submitter email, or name.
- Expose the existing contact metrics (received / notified / failed) unchanged; verify a failure path in tests (ACS returns failed status leads to retry, then `FailedAtUtc` after max attempts).

Realtime / no-polling requirements:

- Not applicable to UI. The dispatcher's background loop is the existing server-side worker; do not add UI polling.

Testing:

- `dotnet build SchwabUrlShortener.slnx` with zero warnings; unit tests for `ReplyTo`, subject sanitizing and truncation, and the failure and retry path; full `dotnet test` in an isolated Compose project.
- `az bicep build` and `az deployment group what-if` clean for each phase (attach the output to the PR description).
- Production acceptance after Phase 3: submit the ziplio.io/contact form with subject `ACS verification <timestamp>`. Confirm the email arrives in `mike.inman@ziplio.io` within 2 minutes with Reply-To set to the submitted address, and that the message headers show `dkim=pass` and `dmarc=pass` for ziplio.io. Michael confirms receipt and the header result.

Documentation:

- Update `docs/runbooks/configuration-and-local-operations.md` with the email architecture, the DNS records added (no secrets), how to check delivery (`az communication email domain show`, Analytics logs), the optional re-queue SQL, and cost.
- Append a dated "Result" section here: PR, what-if and deploy run ids, verification states, the acceptance email result.

Completion criteria:

- [ ] Email service, verified `ziplio.io` domain, `noreply` sender, and linked communication service deployed by Bicep after approved what-ifs.
- [ ] SPF, DKIM, DKIM2 and domain verification all `Verified`; DMARC untouched.
- [ ] Analytics runs `AzureCommunicationEmail` with managed identity; no keys or connection strings anywhere.
- [ ] A live contact submission is received at `mike.inman@ziplio.io` with Reply-To and DKIM/DMARC pass, confirmed by Michael.
- [ ] Runbook and Result section updated; PR merged by Michael and deployed through the gated pipeline.

## Result — 2026-10-14

- Worktree: `/Users/michaelinman/Repos/SchwabUrlShortener-contact-email`, branch `codex/contact-email-acs`, created from `origin/main` `5f92bff`. The handoff checker passed before edits. PR #14 was separately squash-merged into main as `c2218d7`; its checks passed. Track C changes are being reconciled onto that main commit.
- Discovery: Azure CLI account `social@mediaraq.com`, subscription `baaf4acd-ff40-43d1-a7ba-6089a2c4f41e`, resource group `rg-schwab-url-shortener`; `Microsoft.Communication` was `NotRegistered`. Approval to register was requested and is pending. The group's `CanNotDelete` lock remains unchanged. No Communication or Email Service resources existed before this work. Public apex DNS returned one SPF TXT (`v=spf1 include:secureserver.net -all`) and a separate Microsoft 365 tenant TXT.
- Implementation: Added a default-off, two-phase ACS Bicep deployment; default-off Analytics notification settings; managed-identity notifier Reply-To, plain-text bounded subject, ACS operation-ID and sanitized error-code logging; tests and runbook. Built-in `Communication and Email Service Owner` role `09976791-48a7-449e-bb21-39d1a415f350` is the narrowest Communication-specific built-in role returned by `az role definition list`; it is scoped only to the new Communication Service. Microsoft's managed-identity email sample requires Contributor or a custom role with Communication Service read and write, so this role includes the required actions while remaining broader than a future custom send-only role.
- Phase 1 what-if `contact-email-phase1-preview-20261014` exited 0 and predicted four creates (Communication Service with local authentication disabled and no linked domain, Email Service, `ziplio.io` domain, `noreply` sender), 31 existing resources ignored, and no updates or deletes. Explicit approval to deploy was requested and is pending. No Azure deployment has run yet. The domain's exact verification records, verification states, ACS endpoint, and deployment run ID are therefore pending.
- Verification: `az bicep build` passed for both modified templates. `dotnet build SchwabUrlShortener.slnx --no-restore -warnaserror` passed with zero warnings. The isolated `schwab-contact-email-check` Compose project ran all tests against disposable PostgreSQL, Redis, and Kafka: integration 43/43, unit 44/44, Web 85/85. The test containers, network, and volumes were removed. Live inbox delivery, Reply-To, DKIM, DMARC, and Analytics revision health are pending the two approved deployment phases and Michael's DNS and mailbox checks.
